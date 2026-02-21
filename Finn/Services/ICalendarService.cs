using Finn.ViewModels;

namespace Finn.Services
{
    public interface ICalendarService
    {
        void Initialize(CalendarViewModel calendar, string savePath);
    }
}
