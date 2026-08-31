using System;
using System.Globalization;
using Spark.Api;

namespace Spark.Nodes.Core;

/// <summary>
/// Dates and times.
/// </summary>
/// <remarks>
/// <para>
/// <b>A design tool needs these for labelling, sequencing and arithmetic on schedules</b> — a
/// drawing stamp, a phase that starts eight weeks after another, a name built from today's date.
/// They are not geometry, and the set is chosen to cover those uses rather than to wrap
/// <see cref="System.DateTime"/>.
/// </para>
/// <para>
/// <b>Everything here is local time and nothing here is a time zone.</b> A graph that needs an
/// instant comparable across the world is asking a question this node set does not answer, and
/// answering it badly — with a <see cref="DateTimeKind.Unspecified"/> value that silently means
/// whichever machine ran the graph — would be worse than not answering. <see cref="Now"/> says so
/// in its own remarks.
/// </para>
/// <para>
/// <b>Formatting is invariant unless a format string says otherwise.</b> A graph is a document: it
/// is opened on other people's machines, and a date that reads <c>03/04/2026</c> in London and
/// <c>04/03/2026</c> in New York is a defect that only shows up after the file has been shared.
/// </para>
/// </remarks>
[SparkNode(Category = NodeCategories.Input)]
public static class DateAndTime
{
    /// <summary>
    /// The current local date and time.
    /// </summary>
    /// <returns>The moment the node ran.</returns>
    /// <remarks>
    /// <b>Declared as a side effect, and it must be.</b> The evaluation cache is keyed by
    /// provenance rather than by value, and this node has no inputs — so without the declaration
    /// its key never changes, it is computed once, and every later run serves the first answer. A
    /// clock that stops at the moment you placed it is the exact failure
    /// <see cref="NodeSideEffectAttribute"/> exists to prevent, and its own documentation names the
    /// clock as the case.
    /// </remarks>
    [SparkNode(Name = "DateTime.Now")]
    [NodeSideEffect("the system clock")]
    [return: NodePort("dateTime")]
    public static DateTime Now() => DateTime.Now;

    /// <summary>
    /// Today's date, at midnight.
    /// </summary>
    /// <returns>Today at 00:00.</returns>
    /// <remarks>
    /// A side effect for the same reason as <see cref="Now"/>. It changes far more slowly, which
    /// makes it worse rather than better: a graph left open overnight would keep yesterday's date
    /// and nothing on screen would say so.
    /// </remarks>
    [SparkNode(Name = "DateTime.Today")]
    [NodeSideEffect("the system clock")]
    [return: NodePort("dateTime")]
    public static DateTime Today() => DateTime.Today;

    /// <summary>Builds a date and time from its parts.</summary>
    /// <param name="year">The year, 1 to 9999.</param>
    /// <param name="month">The month, 1 to 12.</param>
    /// <param name="day">The day of the month, 1 to the number of days in that month.</param>
    /// <param name="hour">The hour, 0 to 23.</param>
    /// <param name="minute">The minute, 0 to 59.</param>
    /// <param name="second">The second, 0 to 59.</param>
    /// <param name="millisecond">The millisecond, 0 to 999.</param>
    /// <returns>The date and time.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Any part is outside its range, or the day does not exist in that month.
    /// </exception>
    /// <remarks>
    /// The defaults are the first instant of year 1 rather than anything nearer to now, so a node
    /// that has just been placed produces a value that is obviously a placeholder. A default of
    /// today's date would be a side effect wearing a default's clothes.
    /// </remarks>
    [SparkNode(Name = "DateTime.ByDateAndTime")]
    [return: NodePort("dateTime")]
    public static DateTime ByDateAndTime(
        int year = 1,
        int month = 1,
        int day = 1,
        int hour = 0,
        int minute = 0,
        int second = 0,
        int millisecond = 0)
    {
        try
        {
            return new DateTime(year, month, day, hour, minute, second, millisecond, DateTimeKind.Local);
        }
        catch (ArgumentOutOfRangeException error)
        {
            // Rethrown with the parts in the message. .NET's own says only which argument was out
            // of range, and "day" does not tell somebody that the 31st does not exist in April.
            throw new ArgumentOutOfRangeException(
                error.ParamName,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "{0:D4}-{1:D2}-{2:D2} {3:D2}:{4:D2}:{5:D2}.{6:D3} is not a date.",
                    year, month, day, hour, minute, second, millisecond));
        }
    }

    /// <summary>Takes a date and time apart.</summary>
    /// <param name="dateTime">The date and time.</param>
    /// <param name="year">Its year.</param>
    /// <param name="month">Its month, 1 to 12.</param>
    /// <param name="day">Its day of the month.</param>
    /// <param name="hour">Its hour, 0 to 23.</param>
    /// <param name="minute">Its minute.</param>
    /// <param name="second">Its second.</param>
    /// <param name="millisecond">Its millisecond.</param>
    /// <remarks>
    /// Seven output ports rather than a list, because they are seven different things and a user
    /// pulling "the month" out of a list by index is doing arithmetic to ask a question they
    /// already knew the answer to.
    /// </remarks>
    [SparkNode(Name = "DateTime.Components")]
    public static void Components(
        DateTime dateTime,
        out int year,
        out int month,
        out int day,
        out int hour,
        out int minute,
        out int second,
        out int millisecond)
    {
        year = dateTime.Year;
        month = dateTime.Month;
        day = dateTime.Day;
        hour = dateTime.Hour;
        minute = dateTime.Minute;
        second = dateTime.Second;
        millisecond = dateTime.Millisecond;
    }

    /// <summary>Reads a date and time out of text.</summary>
    /// <param name="text">The text, in the invariant round-trip form or anything else invariant.</param>
    /// <returns>The date and time.</returns>
    /// <exception cref="FormatException">The text is not a date.</exception>
    /// <remarks>
    /// <b>Parsed with the invariant culture</b>, for the reason the class remarks give: a graph
    /// travels, and a node that read <c>03/04/2026</c> differently depending on who opened the file
    /// would be a defect nobody could reproduce.
    /// </remarks>
    [SparkNode(Name = "DateTime.FromString")]
    [return: NodePort("dateTime")]
    public static DateTime FromString(string text = "")
    {
        if (!DateTime.TryParse(
                text ?? string.Empty,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTime parsed))
        {
            throw new FormatException($"'{text}' is not a date and time.");
        }

        return parsed;
    }

    /// <summary>Renders a date and time as text.</summary>
    /// <param name="dateTime">The date and time.</param>
    /// <param name="format">
    /// A .NET format string — <c>yyyy-MM-dd</c>, <c>dd MMM yyyy</c>, <c>HH:mm</c>. Empty gives the
    /// sortable round-trip form.
    /// </param>
    /// <returns>The rendered date.</returns>
    /// <exception cref="FormatException">The format string is not one.</exception>
    /// <remarks>
    /// The default is <c>s</c>, the sortable pattern, because a date used as text in a graph is
    /// most often going into a file name or a label that wants to sort.
    /// </remarks>
    [SparkNode(Name = "DateTime.Format")]
    [return: NodePort("text")]
    public static string Format(DateTime dateTime, string format = "s") =>
        dateTime.ToString(
            string.IsNullOrEmpty(format) ? "s" : format,
            CultureInfo.InvariantCulture);

    /// <summary>Moves a date and time forward by a span.</summary>
    /// <param name="dateTime">The date and time.</param>
    /// <param name="timeSpan">How far to move it. A negative span moves it back.</param>
    /// <returns>The moved date and time.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The result is outside the representable range.</exception>
    [SparkNode(Name = "DateTime.AddTimeSpan")]
    [return: NodePort("dateTime")]
    public static DateTime AddTimeSpan(DateTime dateTime, TimeSpan timeSpan) => dateTime + timeSpan;

    /// <summary>Moves a date and time back by a span.</summary>
    /// <param name="dateTime">The date and time.</param>
    /// <param name="timeSpan">How far to move it back.</param>
    /// <returns>The moved date and time.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The result is outside the representable range.</exception>
    [SparkNode(Name = "DateTime.SubtractTimeSpan")]
    [return: NodePort("dateTime")]
    public static DateTime SubtractTimeSpan(DateTime dateTime, TimeSpan timeSpan) =>
        dateTime - timeSpan;

    /// <summary>Which day of the week a date falls on.</summary>
    /// <param name="dateTime">The date.</param>
    /// <returns>The day's English name — <c>Monday</c> through <c>Sunday</c>.</returns>
    /// <remarks>
    /// Text rather than a number, because the numbering is the thing people get wrong: .NET starts
    /// its week on Sunday at zero, ISO 8601 starts on Monday at one, and a node returning <c>3</c>
    /// would be right under one of those and wrong under the other with nothing to say which.
    /// </remarks>
    [SparkNode(Name = "DateTime.DayOfWeek")]
    [return: NodePort("day")]
    public static string DayOfWeek(DateTime dateTime) =>
        dateTime.DayOfWeek.ToString();

    /// <summary>Which day of the year a date falls on, 1 to 366.</summary>
    /// <param name="dateTime">The date.</param>
    /// <returns>The day of the year.</returns>
    [SparkNode(Name = "DateTime.DayOfYear")]
    [return: NodePort("day")]
    public static int DayOfYear(DateTime dateTime) => dateTime.DayOfYear;

    /// <summary>How many days a month has.</summary>
    /// <param name="year">The year, which decides February.</param>
    /// <param name="month">The month, 1 to 12.</param>
    /// <returns>The number of days.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The year or the month is outside its range.</exception>
    [SparkNode(Name = "DateTime.DaysInMonth")]
    [return: NodePort("days")]
    public static int DaysInMonth(int year = 2000, int month = 1) =>
        DateTime.DaysInMonth(year, month);

    /// <summary>Whether a year is a leap year.</summary>
    /// <param name="year">The year, 1 to 9999.</param>
    /// <returns>True when February has 29 days.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The year is outside 1..9999.</exception>
    [SparkNode(Name = "DateTime.IsLeapYear")]
    [return: NodePort("isLeapYear")]
    public static bool IsLeapYear(int year = 2000) => DateTime.IsLeapYear(year);
}

/// <summary>
/// Spans of time: the difference between two dates, and arithmetic on durations.
/// </summary>
/// <remarks>
/// A span is not a date. It is how long something takes, which is what a schedule is made of, and
/// keeping the two apart is why <see cref="DateAndTime.AddTimeSpan"/> takes one rather than seven
/// numbers.
/// </remarks>
[SparkNode(Category = NodeCategories.Input)]
public static class Duration
{
    /// <summary>Builds a span from its parts.</summary>
    /// <param name="days">Whole days.</param>
    /// <param name="hours">Hours.</param>
    /// <param name="minutes">Minutes.</param>
    /// <param name="seconds">Seconds.</param>
    /// <param name="milliseconds">Milliseconds.</param>
    /// <returns>The span, which is negative when the parts sum to less than zero.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The parts sum outside the representable range.</exception>
    /// <remarks>
    /// The parts are <b>not</b> range-checked against a clock: 90 minutes is an hour and a half,
    /// not an error, because that is how somebody types "an hour and a half" without doing the
    /// arithmetic first.
    /// </remarks>
    [SparkNode(Name = "TimeSpan.Create")]
    [return: NodePort("timeSpan")]
    public static TimeSpan Create(
        int days = 0,
        int hours = 0,
        int minutes = 0,
        int seconds = 0,
        int milliseconds = 0) =>
        new(days, hours, minutes, seconds, milliseconds);

    /// <summary>How long there is between two dates.</summary>
    /// <param name="start">The earlier date.</param>
    /// <param name="end">The later date.</param>
    /// <returns>The span, which is negative when <paramref name="end"/> is before <paramref name="start"/>.</returns>
    /// <remarks>
    /// Negative rather than absolute. "How long until the deadline" and "how long since it passed"
    /// are the same question, and a node that returned the magnitude would answer neither.
    /// </remarks>
    [SparkNode(Name = "TimeSpan.ByDateDifference")]
    [return: NodePort("timeSpan")]
    public static TimeSpan ByDateDifference(DateTime start, DateTime end) => end - start;

    /// <summary>Takes a span apart.</summary>
    /// <param name="timeSpan">The span.</param>
    /// <param name="days">Its whole days.</param>
    /// <param name="hours">Its hours, 0 to 23.</param>
    /// <param name="minutes">Its minutes, 0 to 59.</param>
    /// <param name="seconds">Its seconds, 0 to 59.</param>
    /// <param name="milliseconds">Its milliseconds, 0 to 999.</param>
    /// <remarks>
    /// These are the <i>parts</i>, not totals: a span of 36 hours reports 1 day and 12 hours. For
    /// 36, use <see cref="TotalHours"/>. The distinction is the one people get wrong, so both are
    /// here rather than only the one that is easier to explain.
    /// </remarks>
    [SparkNode(Name = "TimeSpan.Components")]
    public static void Components(
        TimeSpan timeSpan,
        out int days,
        out int hours,
        out int minutes,
        out int seconds,
        out int milliseconds)
    {
        days = timeSpan.Days;
        hours = timeSpan.Hours;
        minutes = timeSpan.Minutes;
        seconds = timeSpan.Seconds;
        milliseconds = timeSpan.Milliseconds;
    }

    /// <summary>The whole span expressed in days, including the fraction.</summary>
    /// <param name="timeSpan">The span.</param>
    /// <returns>The number of days, which is 1.5 for thirty-six hours.</returns>
    [SparkNode(Name = "TimeSpan.TotalDays")]
    [return: NodePort("days")]
    public static double TotalDays(TimeSpan timeSpan) => timeSpan.TotalDays;

    /// <summary>The whole span expressed in hours, including the fraction.</summary>
    /// <param name="timeSpan">The span.</param>
    /// <returns>The number of hours, which is 36 for a day and a half.</returns>
    [SparkNode(Name = "TimeSpan.TotalHours")]
    [return: NodePort("hours")]
    public static double TotalHours(TimeSpan timeSpan) => timeSpan.TotalHours;

    /// <summary>The whole span expressed in minutes, including the fraction.</summary>
    /// <param name="timeSpan">The span.</param>
    /// <returns>The number of minutes.</returns>
    [SparkNode(Name = "TimeSpan.TotalMinutes")]
    [return: NodePort("minutes")]
    public static double TotalMinutes(TimeSpan timeSpan) => timeSpan.TotalMinutes;

    /// <summary>The whole span expressed in seconds, including the fraction.</summary>
    /// <param name="timeSpan">The span.</param>
    /// <returns>The number of seconds.</returns>
    [SparkNode(Name = "TimeSpan.TotalSeconds")]
    [return: NodePort("seconds")]
    public static double TotalSeconds(TimeSpan timeSpan) => timeSpan.TotalSeconds;
}
