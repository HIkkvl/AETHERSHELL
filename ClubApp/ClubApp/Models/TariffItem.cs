namespace AetherShell.Client.Models
{
    public class TariffItem
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public int DurationMinutes { get; set; }
        public decimal Price { get; set; }

        // Новые поля для пакетов
        public int? StartHour { get; set; }
        public int? EndHour { get; set; }
        public bool IsFixedTime { get; set; }

        /// <summary>Сгораемый пакет: остаток не сохраняется на аккаунт.</summary>
        public bool IsBurnable { get; set; }

        // Фичи для отображения на карточке
        public string Feature1 { get; set; } = "Vip зона";
        public string Feature2 { get; set; } = "144Hz монитор";

        public string BurnLabel => IsBurnable ? "Сгораемый" : "Несгораемый";

        // --- ИСПРАВЛЕННОЕ СВОЙСТВО ---
        public string TimeDisplay
        {
            get
            {
                // 1. САМОЕ ГЛАВНОЕ: Смотрим на тип тарифа, а не на цифры.
                // Если IsFixedTime == false (галочка "Фиксированное время" снята), 
                // то это всегда круглосуточный тариф.
                if (!IsFixedTime)
                {
                    return "Круглосуточно";
                }

                // 2. Дополнительная защита: если это пакет, но часы не заданы (null)
                if (StartHour == null || EndHour == null)
                {
                    return "Круглосуточно";
                }

                // 3. Если время начала и конца совпадают (например 0 и 0), и это пакет
                if (StartHour == EndHour)
                {
                    return "Суточный"; // Или верните "Круглосуточно", как вам удобнее
                }

                // 4. Если мы дошли сюда, значит это ПАКЕТ с конкретным временем.
                // Используем GetValueOrDefault(), чтобы получить число, даже если там null
                return $"{StartHour.GetValueOrDefault():00}:00 - {EndHour.GetValueOrDefault():00}:00";
            }
        }
    }
}