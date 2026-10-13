using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ChessLens.Core.Analysis;
using ChessLens.Core.Games;
using ChessLens.Core.Insights;

namespace ChessLens.Core.Coaching;

public static class CoachingRules
{
    public const string Version = "chesslens-coaching-v1";
    public const int MaximumInputBytes = 32_768;
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static CoachingEvidence Build(SourceGame game, AnalysisRun run, MoveAnalysis move, string level)
    {
        if (level is not ("beginner" or "intermediate" or "advanced")) throw new ArgumentException("Choose a recognised coaching level.");
        var verified = VerifiedEvidence.Read(game, run, move) ?? throw new ArgumentException("A completed, non-provisional review with consistent legal evidence is required for coaching.");
        var rating = move.Side == "white" ? game.WhiteRating : game.BlackRating;
        var knownPool = game.RatingPool is "bullet" or "blitz" or "rapid" or "classical" or "daily" or "correspondence";
        var adjusted = knownPool && rating is not null && game.Provider is "chesscom" or "lichess";
        var band = !adjusted ? 0 : rating < (game.Provider == "chesscom" ? 1000 : 1300) ? -1 : rating >= (game.Provider == "chesscom" ? 1600 : 1900) ? 1 : 0;
        var length = level switch { "beginner" => 2 + Math.Max(0, band), "intermediate" => 5 + band, _ => 8 + band * 2 };
        var line = verified.BestLine.Pv.Take(length).ToArray();
        var board = ChessRules.Replay(verified.Position.InitialFen, verified.Position.History);
        var san = line.Select(uci => ChessRules.PlayUci(board, uci).San ?? uci).ToArray();
        var who = move.Side == "white" ? "White" : "Black";
        var facts = new List<CoachingFact> { new("played-decision", "what", $"{who} played {verified.SourceMove.San}. ChessLens labels this decision {move.Classification}.") };
        if (move.MateTransition == "allowed-forced-mate") facts.Add(new("allowed-mate", "why", "The played move allowed a forced mate that the checked alternative avoided."));
        else if (move.MateTransition == "missed-forced-mate") facts.Add(new("missed-mate", "why", "A verified mating continuation was available, but the played move did not preserve it."));
        else if (move.CentipawnLoss is { } loss)
        {
            facts.Add(new("evaluation-loss", "why", loss == 0 ? "The played move preserved the checked evaluation."
                : level == "beginner" ? $"The checked alternative kept a better position: about {loss / 100d:0.00} pawns of engine evaluation."
                : $"The played continuation lost {loss} centipawns ({loss / 100d:0.00} pawns of evaluation) relative to the checked alternative."));
        }
        else facts.Add(new("mate-context", "why", move.MateTransition == "mate-maintained" ? "The move preserved a forced mating continuation." : "The saved review still faces a forced mate; a later mate is not treated as a centipawn gain."));
        var insight = InsightRules.Build([new(game, move.Side, run)], new InsightOptions()).Evidence.SingleOrDefault(e => e.Ply == move.Ply);
        if (insight?.Tags.Contains("hanging-material") == true)
            facts.Add(new("material-loss", "why", "The checked played line loses material that the alternative preserves through the exchanges."));
        if (insight?.Tags.Contains("missed-tactics") == true && move.MateTransition != "missed-forced-mate")
            facts.Add(new("tactical-opportunity", "why", "The checked alternative has a sustained material-winning tactical opportunity."));
        var lessons = new List<CoachingLesson> { new("check-replies", level switch
        {
            "beginner" => "Before moving, look for your opponent's checks, captures and immediate threats.",
            "intermediate" => "Compare candidate moves by calculating forcing replies, then check what remains after the exchanges.",
            _ => "Test the strongest defensive reply to each candidate before trusting the resulting evaluation."
        }) };
        if (insight?.Tags.Contains("missed-tactics") == true) lessons.Add(new("forcing-first", "Start with checks and captures, and calculate the opponent's strongest response before committing."));
        if (insight?.Tags.Contains("hanging-material") == true) lessons.Add(new("material-check", "Check whether your material remains safe after the opponent's forcing replies and expected recaptures."));
        if (insight?.Tags.Contains("opening-mistakes") == true) lessons.Add(new("opening-threat", "Explain the immediate threat in this opening position before trying to memorize a move."));
        var context = new CoachingRating(game.Provider, game.RatingPool, game.TimeControl, rating, adjusted);
        var basis = JsonSerializer.Serialize(new { gameId = game.Id, runId = run.Id, move.Ply, move.FenBefore, move.PlayedMove, move.BestMove,
            move.BestSearchJson, move.PlayedSearchJson, move.BudgetJson, run.AnalysisVersion, run.ClassificationSettingsJson, level, context, line, facts, lessons, version = Version });
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(basis)));
        return new(hash, game.Id, run.Id, move.Ply, move.FenBefore, move.Side, move.PlayedMove, move.BestMove, level, context, line, san,
            facts.ToArray(), lessons.ToArray(), run.AnalysisVersion, verified.BestSearch.Engine, verified.BestSearch.SettingsJson,
            run.ClassificationSettingsJson ?? JsonSerializer.Serialize(new AnalysisOptions()), Version);
    }

    public static CoachingExplanation Factual(CoachingEvidence evidence, string? fallbackReason = null)
        => Render(evidence, ["played-decision", evidence.Facts.First(f => f.Section == "why").Id], evidence.Line,
            evidence.Lessons[0].Id, "factual", fallbackReason, null);

    public static CoachingExplanation? Validate(string json, CoachingEvidence evidence, string model)
    {
        try
        {
            if (Encoding.UTF8.GetByteCount(json) > 16_384) return null;
            var plan = JsonSerializer.Deserialize<CoachingPlan>(json, Json);
            if (plan is null || plan.EvidenceId != evidence.Id || plan.Level != evidence.Level || plan.PlayedMove != evidence.PlayedMove ||
                plan.FactIds.Length is < 2 or > 4 || plan.FactIds.Distinct().Count() != plan.FactIds.Length ||
                !plan.FactIds.Contains("played-decision") || plan.FactIds.Any(id => !evidence.Facts.Any(f => f.Id == id)) ||
                !plan.FactIds.Any(id => evidence.Facts.Any(f => f.Id == id && f.Section == "why")) ||
                plan.BetterLine.Length is < 1 || plan.BetterLine.Length > evidence.Line.Length ||
                !plan.BetterLine.SequenceEqual(evidence.Line.Take(plan.BetterLine.Length)) || !evidence.Lessons.Any(l => l.Id == plan.LessonId)) return null;
            return Render(evidence, plan.FactIds, plan.BetterLine, plan.LessonId, "model-selected", null, model);
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException) { return null; }
    }

    private static CoachingExplanation Render(CoachingEvidence evidence, string[] facts, string[] line, string lesson, string mode, string? fallback, string? model)
        => new(evidence.Id, evidence.GameId, evidence.RunId, evidence.Ply, evidence.Fen, evidence.Level, evidence.Rating,
            string.Join(' ', facts.Select(id => evidence.Facts.Single(f => f.Id == id)).Where(f => f.Section == "what").Select(f => f.Text)),
            string.Join(' ', facts.Select(id => evidence.Facts.Single(f => f.Id == id)).Where(f => f.Section == "why").Select(f => f.Text)),
            line, evidence.SanLine.Take(line.Length).ToArray(), evidence.Lessons.Single(l => l.Id == lesson).Text,
            mode, fallback, evidence.PromptVersion, model, evidence.AnalysisVersion, evidence.Engine);
}
