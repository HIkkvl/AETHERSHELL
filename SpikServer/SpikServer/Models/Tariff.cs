using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AetherShell.Server.Models
{
    public class Tariff
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string Name { get; set; } = string.Empty; 

        public int DurationMinutes { get; set; } 

        [Column(TypeName = "decimal(18,2)")]
        public decimal Price { get; set; } 

        public bool IsActive { get; set; } = true; 

        public int? StartHour { get; set; } 
        public int? EndHour { get; set; }

        public bool IsFixedTime { get; set; }

        /// <summary>
        /// Сгораемый пакет: недоигранные минуты не сохраняются на аккаунт.
        /// Несгораемый — остаток уходит в RemainingMinutes клиента.
        /// </summary>
        public bool IsBurnable { get; set; } = false;

        // Фичи для отображения на карточке тарифа
        public string Feature1 { get; set; } = "Vip зона";
        public string Feature2 { get; set; } = "144Hz монитор";
    }
}