using System; 

namespace AetherShell.Server.Data
{
    public enum ComputerStatus
    {
        Offline,    // Не в сети
        Locked,     // В сети, заблокирован (нет активной сессии)
        Active,     // В сети, активная сессия
        Error       // Ошибка (был онлайн, но пропал без корректного отключения)
    }

    public class Computer
    {
        public int Id { get; set; }

        public string Name { get; set; } = "";

        /// <summary>
        /// Стабильный идентификатор машины, который шелл генерирует при первой установке
        /// и хранит локально. В отличие от MAC не меняется при смене сетевой карты
        /// и не пересекается между клубами.
        /// </summary>
        public string? HardwareId { get; set; }

        public string DisplayName { get; set; } = "";
        public string GroupName { get; set; } = "Общий зал";
        public string MacAddress { get; set; } = "";

        /// <summary>
        /// Позиция на карте клуба (проценты холста 0…100, центр плитки).
        /// null — ещё не расставлен, UI раскладывает автосеткой.
        /// </summary>
        public double? MapX { get; set; }
        public double? MapY { get; set; }

        public bool IsOnline { get; set; }

        // Новые поля для статуса и подтверждения
        public ComputerStatus Status { get; set; } = ComputerStatus.Offline;
        public bool IsApproved { get; set; } = false;  // Подтверждён администратором
        public DateTime? LastSeenAt { get; set; }       // Когда последний раз был в сети
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public string? CurrentUser { get; set; }
        public DateTime? SessionEndTime { get; set; }

        /// <summary>
        /// Текущая сессия сохраняет остаток минут на аккаунт при остановке
        /// (несгораемый пакет). false — сгораемый: остаток пропадает.
        /// </summary>
        public bool SessionSavesRemaining { get; set; } = true;

        /// <summary>Название тарифа текущей сессии — для сайдбара шелла.</summary>
        public string? CurrentTariffName { get; set; }

        // Что запущено на ПК прямо сейчас: шелл сообщает активное окно, а панель
        // показывает это вместо безликого «в игре».
        public string? CurrentApp { get; set; }
        public string? CurrentAppTitle { get; set; }
        public DateTime? CurrentAppSince { get; set; }

        
        // Системная информация о ПК
        public string? IpAddress { get; set; }
        public string? CpuName { get; set; }           // Процессор
        public int? RamTotalMb { get; set; }           // Общий объём RAM в МБ
        public int? RamUsedMb { get; set; }            // Используемый объём RAM в МБ
        public string? GpuName { get; set; }           // Видеокарта
        public string? DiskInfo { get; set; }          // Информация о дисках (JSON или текст)
        public string? OsVersion { get; set; }         // Версия ОС
        public DateTime? SystemInfoUpdatedAt { get; set; } // Когда обновлялась инфа
    }
}