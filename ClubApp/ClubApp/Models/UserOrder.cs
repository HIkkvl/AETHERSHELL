using System.Collections.Generic;
using System.ComponentModel;

namespace AetherShell.Client.Models
{
    public partial class UserOrder : INotifyPropertyChanged
    {
        public int Id { get; set; }
        public decimal TotalPrice { get; set; }

        private string _status;

        public string Status
        {
            get => _status;
            set
            {
                if (_status != value)
                {
                    _status = value;
                    OnPropertyChanged(nameof(Status));
                    OnPropertyChanged(nameof(StatusText));
                    OnPropertyChanged(nameof(StatusColor));
                }
            }
        }

        public string Time { get; set; }
        public List<UserOrderItem> Items { get; set; } = new List<UserOrderItem>();

        // --- Вычисляемые свойства для UI ---

        public string StatusText
        {
            get
            {
                // Нормализуем и маппим статусы в русские подписи
                var s = _status?.Trim().ToLower();

                return s switch
                {
                    "new" => "В очереди",
                    "processing" => "Готовится",
                    "ready" => "Готов",
                    "completed" => "Выдано",
                    "cancelled" => "Отменен",
                    _ => _status ?? "Неизвестно"
                };
            }
        }

        public string StatusColor
        {
            get
            {
                var s = _status?.Trim().ToLower();

                return s switch
                {
                    "new" => "#FFFFFF",
                    "processing" => "#E04BC0",
                    "ready" => "#00FF00",
                    "cancelled" => "#FF4444",
                    "completed" => "#888888",
                    _ => "#AAAAAA"
                };
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public class UserOrderItem
    {
        public string ProductNameSnapshot { get; set; }
        public int Quantity { get; set; }
    }
}
