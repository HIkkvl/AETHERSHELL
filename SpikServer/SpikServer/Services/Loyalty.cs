using AetherShell.Server.Models;

namespace AetherShell.Server.Services
{
    /// <summary>
    /// Прогрессивная скидка за накопленные траты. Единственное место, где живёт эта формула:
    /// раньше она была продублирована в трёх местах с расходящимися вычислениями.
    ///
    /// Каждый следующий процент стоит дороже предыдущего: первый процент даётся за
    /// LoyaltyFirstThreshold, каждый следующий порог больше на LoyaltyStep.
    /// </summary>
    public static class Loyalty
    {
        public static int DiscountPercent(decimal totalSpent, Club? club)
        {
            var threshold = club?.LoyaltyFirstThreshold ?? 50000m;
            var step = club?.LoyaltyStep ?? 5000m;
            var maxPercent = club?.MaxDiscountPercent ?? 20;

            if (threshold <= 0 || totalSpent < threshold) return 0;

            var remaining = totalSpent;
            var percent = 0;

            while (remaining >= threshold && percent < maxPercent)
            {
                percent++;
                remaining -= threshold;
                threshold += step;
            }

            return percent;
        }

        /// <summary>
        /// Скидка: ручная корректировка → скидка группы → лояльность по тратам.
        /// </summary>
        public static int EffectiveDiscount(Client client, Club? club)
        {
            var maxPercent = club?.MaxDiscountPercent ?? 20;

            if (client.DiscountOverride is int manual)
                return Clamp(manual, maxPercent);

            if (client.Group?.DiscountPercent is int groupPct)
                return Clamp(groupPct, maxPercent);

            return DiscountPercent(client.TotalSpent, club);
        }

        /// <summary>
        /// Сколько ещё нужно потратить до следующего процента скидки.
        /// <c>null</c>, если максимум уже достигнут или скидка задана вручную/группой.
        /// </summary>
        public static decimal? NextThreshold(Client client, Club? club)
        {
            if (client.DiscountOverride.HasValue) return null;
            if (client.Group?.DiscountPercent.HasValue == true) return null;
            return NextThreshold(client.TotalSpent, club);
        }

        public static decimal ApplyDiscount(decimal price, int discountPercent)
            => price - (price * discountPercent / 100);

        public static decimal? NextThreshold(decimal totalSpent, Club? club)
        {
            var threshold = club?.LoyaltyFirstThreshold ?? 50000m;
            var step = club?.LoyaltyStep ?? 5000m;
            var maxPercent = club?.MaxDiscountPercent ?? 20;

            if (threshold <= 0 || maxPercent <= 0) return null;

            var remaining = totalSpent;
            var percent = 0;

            while (remaining >= threshold && percent < maxPercent)
            {
                percent++;
                remaining -= threshold;
                threshold += step;
            }

            return percent >= maxPercent ? null : threshold - remaining;
        }

        private static int Clamp(int value, int maxPercent)
        {
            if (value < 0) return 0;
            if (value > maxPercent) return maxPercent;
            return value;
        }
    }
}
