using csmhelper.Models;

namespace csmhelper.services
{
    /// <summary>
    /// Calculates working time, respecting lunch breaks and weekends.
    /// Direct C# port of the Python WorkDayCalculator class.
    /// </summary>
    internal class WorkDayCalculator
    {
        private readonly GantEmployee _employee;

        public WorkDayCalculator(GantEmployee employee)
        {
            _employee = employee;
        }

        public bool IsWorkday(DateTime dt)
        {
            if (dt.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) return false;
            // День отпуска — не рабочий
            if (_employee.IsOnVacation(dt)) return false;
            return true;
        }

        public bool IsWorkingHours(DateTime dt)
        {
            if (!IsWorkday(dt)) return false;
            var t = TimeOnly.FromDateTime(dt);

            // Check lunch break
            if (t >= _employee.LunchStart && t < _employee.LunchEnd) return false;

            // Check working hours
            return t >= _employee.WorkStart && t < _employee.WorkEnd;
        }

        public DateTime AddWorkMinutes(DateTime start, double minutes)
        {
            var current = start;
            var remaining = minutes;

            while (remaining > 0)
            {
                // Skip non-working time
                while (!IsWorkingHours(current))
                {
                    current = MoveToNextWorkingMoment(current);
                }

                var dayEnd = current.Date.Add(_employee.WorkEnd.ToTimeSpan());
                var lunchStart = current.Date.Add(_employee.LunchStart.ToTimeSpan());
                var lunchEnd = current.Date.Add(_employee.LunchEnd.ToTimeSpan());

                // Calculate available minutes in current day
                var availableMinutes = (dayEnd - current).TotalMinutes;

                // Subtract lunch if it falls within the remaining window
                if (current < lunchStart && dayEnd > lunchEnd)
                {
                    availableMinutes -= (_employee.LunchEnd.Hour - _employee.LunchStart.Hour) * 60;
                }

                if (remaining <= availableMinutes)
                {
                    current = current.AddMinutes(remaining);
                    remaining = 0;
                }
                else
                {
                    remaining -= availableMinutes;
                    current = NextWorkdayStart(current.Date.AddDays(1));
                }
            }

            return current;
        }

        public DateTime CalculateEndTime(DateTime start, double durationHours)
            => AddWorkMinutes(start, durationHours * 60);

        private DateTime MoveToNextWorkingMoment(DateTime dt)
        {
            if (!IsWorkday(dt))
                return NextWorkdayStart(dt);

            var t = TimeOnly.FromDateTime(dt);

            // Before work starts
            if (t < _employee.WorkStart)
                return dt.Date.Add(_employee.WorkStart.ToTimeSpan());

            // After work ends
            if (t >= _employee.WorkEnd)
                return NextWorkdayStart(dt.Date.AddDays(1));

            // During lunch break
            if (t >= _employee.LunchStart && t < _employee.LunchEnd)
                return dt.Date.Add(_employee.LunchEnd.ToTimeSpan());

            // Already in working hours
            return dt;
        }

        private DateTime NextWorkdayStart(DateTime from)
        {
            var d = from.Date;
            while (!IsWorkday(d)) d = d.AddDays(1);
            return d.Add(_employee.WorkStart.ToTimeSpan());
        }
    }
}