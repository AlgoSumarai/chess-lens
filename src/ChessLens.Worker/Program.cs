using ChessLens.Worker;
using ChessLens.Core.Imports;
using ChessLens.Core.Analysis;
using ChessLens.Core.Persistence;
using ChessLens.Core.Insights;
using ChessLens.Core.Training;
using Microsoft.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddDbContext<LensDbContext>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("Database")
    ?? throw new InvalidOperationException("ConnectionStrings__Database is required.")));
builder.Services.AddSingleton<PgnReader>();
builder.Services.AddScoped<ImportProcessor>();
builder.Services.Configure<EngineOptions>(builder.Configuration.GetSection("Stockfish"));
builder.Services.AddOptions<AnalysisOptions>().Bind(builder.Configuration.GetSection("Analysis"))
    .Validate(o => o.InaccuracyCentipawns >= 1 && o.MistakeCentipawns > o.InaccuracyCentipawns &&
        o.BlunderCentipawns > o.MistakeCentipawns && o.BlunderCentipawns <= 10_000, "Classification thresholds must be strictly increasing.")
    .ValidateOnStart();
builder.Services.AddSingleton<IChessEngine, EnginePool>();
builder.Services.AddScoped<AnalysisProcessor>();
builder.Services.AddScoped<InsightProcessor>();
builder.Services.AddScoped<PuzzleProcessor>();
builder.Services.AddHostedService<ImportWorker>();
builder.Services.AddScoped<PositionAnalysisProcessor>();
builder.Services.AddHostedService<PositionWorker>();

var host = builder.Build();
host.Run();
