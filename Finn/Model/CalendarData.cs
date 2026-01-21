using Avalonia.Media;
using Newtonsoft.Json.Bson;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;

namespace Finn.Model
{
    public class CalendarData : INotifyPropertyChanged
    {

        private DateOnly date;
        public DateOnly Date
        {
            get { return date; }
            set { date = value; RaisePropertyChanged("Date"); }
        }

        public int WeekOfMonth
        {
            get 
            {

                int firstWeek = ISOWeek.GetWeekOfYear(new DateTime(Date.Year, Date.Month, 1));
                int currentWeek = ISOWeek.GetWeekOfYear(new DateTime(Date.Year, Date.Month, Date.Day));

                return currentWeek - firstWeek;
            }
        }

        public string DateString
        {
            get
            {
                string text = date.ToString();

                if (Date.DayOfWeek == DayOfWeek.Saturday || Date.DayOfWeek == DayOfWeek.Sunday)
                {
                    text = " - ";
                }

                return text;
            }
        }

        public string DateStringIcon
        {
            get
            {
                string text = date.ToString() + "⠀";

                if (HasNote)
                {
                    text = text + "📝 ";
                }

                if (HasTime)
                {
                    text = text + "🕑 ";
                }

                if (Date.DayOfWeek == DayOfWeek.Saturday || Date.DayOfWeek == DayOfWeek.Sunday)
                {
                    text = " - ";
                }
                else
                {
                    text = text + " " + TotalTime;
                }

                return text;
            }
        }


        private string note1 = string.Empty;
        public string Note1
        {
            get { return note1; }
            set { note1 = value; RaisePropertyChanged("Note1"); RaisePropertyChanged("DateString"); }
        }

        private string note2 = string.Empty;
        public string Note2
        {
            get { return note2; }
            set { note2 = value; RaisePropertyChanged("Note2"); RaisePropertyChanged("DateString"); }
        }

        private string reminder = string.Empty;
        public string Reminder
        {
            get { return reminder; }
            set { reminder = value; RaisePropertyChanged("Reminder"); RaisePropertyChanged("DateString"); }
        }

        public int? TotalTime
        {
            get {
                int? time = null;
                if (TimeSheets.Select(x => x.Hours).Sum() != 0)
                {
                    return TimeSheets.Select(x => x.Hours).Sum();
                }
                else
                {
                    return null;
                }
            }
        }

        private ObservableCollection<TimeSheetData> timeSheets = new ObservableCollection<TimeSheetData>();

        public ObservableCollection<TimeSheetData> TimeSheets
        {
            get { return timeSheets; }
            set { timeSheets = value; RaisePropertyChanged("TimeSheets");}
        }

        private string currentTimeSheetProjectDiary = string.Empty;
        public string CurrentTimeSheetProjectDiary
        {
            get { return currentTimeSheetProjectDiary; }
            set { currentTimeSheetProjectDiary = value; RaisePropertyChanged("CurrentTimeSheetProjectDiary"); }
        }

        private int? currentTimeSheetProjectTime = null;
        public int? CurrentTimeSheetProjectTime
        {
            get { return currentTimeSheetProjectTime; }
            set { currentTimeSheetProjectTime = value; RaisePropertyChanged("CurrentTimeSheetProjectTime"); }
        }

        public void TriggerDateStringUpdate()
        {
            RaisePropertyChanged("DateString");
            RaisePropertyChanged("DateStringIcon");
        }

        public SolidColorBrush Foreground
        {
            get
            {
                if (Date.DayOfWeek == DayOfWeek.Friday)
                {
                    return new SolidColorBrush(Avalonia.Media.Color.Parse("#444444"));
                }
                else
                {
                    return new SolidColorBrush(Avalonia.Media.Color.Parse("#444444"));
                }
            } 
        }

        public bool HasNote
        {
            get
            {
                if (Note1.Length > 0 || Note2.Length > 0)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
        }

        public bool HasTime
        {
            get
            {
                if (TimeSheets.Count > 0)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
        }


        public void SetCurrentTimeSheetProjectDiary(string text)
        {

            if (TimeSheets.Where(x => x.Project == text).Count() > 0)
            {
                string diary = string.Empty;
                int time = 0;

                foreach (TimeSheetData timeSheet in TimeSheets.Where(x => x.Project == text))
                {
                    diary = diary + timeSheet.Diary;
                    time = time + timeSheet.Hours;
                }
                CurrentTimeSheetProjectDiary = diary;
                CurrentTimeSheetProjectTime = time;
            }
            else
            {
                CurrentTimeSheetProjectDiary = string.Empty;
                CurrentTimeSheetProjectTime = null;
            }


        }


        private void RaisePropertyChanged(string propName)
        {
            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs(propName));
        }
        public event PropertyChangedEventHandler PropertyChanged;
    }
}
