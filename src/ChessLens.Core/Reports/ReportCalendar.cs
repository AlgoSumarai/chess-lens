namespace ChessLens.Core.Reports;

public sealed record ReportPeriod(string Kind, string? Timezone, DateOnly FromDate, DateOnly ToDate, DateTimeOffset From, DateTimeOffset To);

public static class ReportCalendar
{
    public static DateOnly Monday(DateTimeOffset instant, string timezone)
    {
        var local = TimeZoneInfo.ConvertTime(instant, Resolve(timezone));
        var day = DateOnly.FromDateTime(local.DateTime);
        return day.AddDays(-(((int)day.DayOfWeek + 6) % 7));
    }
    public static ReportPeriod Week(DateOnly monday, string timezone, DateTimeOffset now)
        => Week(monday, Resolve(timezone), now);
    public static ReportPeriod Week(DateOnly monday, TimeZoneInfo zone, DateTimeOffset now)
    {
        if (monday.DayOfWeek != DayOfWeek.Monday) throw new ArgumentException("Choose the Monday beginning the week.");
        var to = monday.AddDays(7);
        var period = new ReportPeriod("weekly", zone.Id, monday, to, Boundary(monday, zone), Boundary(to, zone));
        if (period.To > now) throw new ArgumentException("Weekly reports cover completed local-calendar weeks. This week is still in progress.");
        return period;
    }
    public static ReportPeriod Progress(DateOnly from, DateOnly through)
    {
        if (through < from || through.DayNumber - from.DayNumber > 365 || from.Year < 1900 || through.Year > 2200)
            throw new ArgumentException("Choose up to 366 days, from 1900 through 2200.");
        var to = through.AddDays(1);
        return new("progress", "UTC", from, to, new(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), new(to.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));
    }
    public static ReportPeriod Previous(ReportPeriod period) => period.Kind == "weekly"
        ? Week(period.FromDate.AddDays(-7), period.Timezone!, period.From)
        : Progress(period.FromDate.AddDays(-(period.ToDate.DayNumber - period.FromDate.DayNumber)), period.FromDate.AddDays(-1));

    public static TimeZoneInfo Resolve(string timezone)
    {
        if (string.IsNullOrWhiteSpace(timezone) || timezone.Length > 100) throw new ArgumentException("Choose a recognised timezone.");
        try { return TimeZoneInfo.FindSystemTimeZoneById(timezone); }
        catch (TimeZoneNotFoundException) { throw new ArgumentException("Choose a recognised timezone."); }
        catch (InvalidTimeZoneException) { throw new ArgumentException("That timezone cannot be used for reports."); }
    }
    private static DateTimeOffset Boundary(DateOnly date, TimeZoneInfo zone)
    {
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var advanced = 0;
        while (zone.IsInvalidTime(local))
        {
            if (++advanced > 2880) throw new ArgumentException("This timezone has an unsupported calendar boundary.");
            local = local.AddMinutes(1);
        }
        var offset = zone.IsAmbiguousTime(local) ? zone.GetAmbiguousTimeOffsets(local).Max() : zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }
}
