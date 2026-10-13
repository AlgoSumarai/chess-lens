using ChessLens.Core.Reports;

namespace ChessLens.Tests;

public sealed class ReportCalendarTests
{
    [Theory]
    [InlineData("2026-03-02", "America/New_York", 167)]
    [InlineData("2026-10-26", "America/New_York", 169)]
    [InlineData("2026-10-05", "Africa/Johannesburg", 168)]
    public void Weeks_use_separate_local_midnight_boundaries_and_remain_original_after_timezone_changes(string monday, string timezone, int hours)
    {
        var week = ReportCalendar.Week(DateOnly.Parse(monday), timezone, DateTimeOffset.Parse("2030-01-01T00:00:00Z"));
        Assert.Equal(hours, (week.To - week.From).TotalHours); Assert.Equal(timezone, week.Timezone);
        var other = ReportCalendar.Week(DateOnly.Parse(monday), "Asia/Tokyo", DateTimeOffset.Parse("2030-01-01T00:00:00Z"));
        Assert.NotEqual(other.From, week.From); Assert.Equal(timezone, week.Timezone);
        Assert.Equal(week.FromDate.AddDays(-7), ReportCalendar.Previous(week).FromDate);
        Assert.Equal(week.From, ReportCalendar.Previous(week).To);
    }
    [Fact]
    public void Local_week_and_completed_boundary_are_explicit_and_progress_previous_period_has_equal_duration()
    {
        var now = DateTimeOffset.Parse("2026-10-11T23:30:00Z");
        Assert.Equal(new DateOnly(2026, 10, 12), ReportCalendar.Monday(now, "Africa/Johannesburg"));
        Assert.Equal(new DateOnly(2026, 10, 5), ReportCalendar.Monday(now, "America/New_York"));
        Assert.Throws<ArgumentException>(() => ReportCalendar.Week(new(2026, 10, 12), "UTC", now));
        Assert.Throws<ArgumentException>(() => ReportCalendar.Week(new(2026, 10, 6), "UTC", now));
        Assert.Throws<ArgumentException>(() => ReportCalendar.Resolve("not/a/timezone"));
        var progress = ReportCalendar.Progress(new(2026, 10, 1), new(2026, 10, 12)); var prior = ReportCalendar.Previous(progress);
        Assert.Equal(progress.To - progress.From, prior.To - prior.From); Assert.Equal(progress.From, prior.To);
        Assert.Throws<ArgumentException>(() => new ProgressFilter(new(2026, 1, 1), new(2028, 1, 1)).Validate());
        Assert.Throws<ArgumentException>(() => new ProgressFilter(new(2026, 1, 1), new(2026, 1, 2), Opening: "named", UnknownOpening: true).Validate());
    }
    [Fact]
    public void Labelled_midnight_transition_timezone_uses_first_valid_minute_and_earlier_ambiguous_instant()
    {
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(new(2026, 1, 1), new(2026, 12, 31), TimeSpan.FromHours(1),
            TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new(1, 1, 1, 0, 0, 0), 3, 1, DayOfWeek.Monday),
            TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new(1, 1, 1, 1, 0, 0), 11, 1, DayOfWeek.Monday));
        var zone = TimeZoneInfo.CreateCustomTimeZone("Labelled midnight DST fixture", TimeSpan.Zero, "Fixture", "Fixture standard", "Fixture summer", [rule]);
        var now = DateTimeOffset.Parse("2030-01-01T00:00:00Z");
        var spring = ReportCalendar.Week(new(2026, 3, 2), zone, now);
        Assert.Equal(DateTimeOffset.Parse("2026-03-02T00:00:00Z"), spring.From); Assert.Equal(167, (spring.To - spring.From).TotalHours);
        var autumn = ReportCalendar.Week(new(2026, 11, 2), zone, now);
        Assert.Equal(DateTimeOffset.Parse("2026-11-01T23:00:00Z"), autumn.From); Assert.Equal(169, (autumn.To - autumn.From).TotalHours);
    }
    [Theory]
    [InlineData("179", null, "bullet")]
    [InlineData("180", null, "blitz")]
    [InlineData("599", null, "blitz")]
    [InlineData("600", null, "rapid")]
    [InlineData("1799", null, "rapid")]
    [InlineData("1800", null, "classical")]
    [InlineData("60+3", null, "blitz")]
    [InlineData("0+1", null, "bullet")]
    [InlineData("0", null, "unknown")]
    [InlineData("40/7200:3600", null, "unknown")]
    [InlineData("1/86400", null, "correspondence")]
    [InlineData(null, "daily", "correspondence")]
    [InlineData(null, "correspondence", "correspondence")]
    [InlineData("999999999999999999+5", null, "unknown")]
    [InlineData("NaN", "blitz", "unknown")]
    public void Time_control_grouping_has_transparent_boundaries_and_keeps_unsupported_controls_unknown(string? control, string? pool, string expected) =>
        Assert.Equal(expected, TimeControlGroups.Group(control, pool));
}
