using Finn.ViewModels;

namespace Finn.Services
{
    public interface ICalendarService
    {
        void Initialize(CalendarViewModel calendar, string savePath);
        // Force an immediate save of the current calendar data
        void SaveNow();
    }
}
