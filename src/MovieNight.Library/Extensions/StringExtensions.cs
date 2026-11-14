namespace MovieNight.Library.Extensions;

internal static class StringExtensions
{
    extension(string input)
    {
        public string TrimNullSafe() =>
            string.IsNullOrWhiteSpace(input) ? string.Empty : input.Trim();

        /// <summary>
        /// The inbox an address delivers to, for the question of how many nights one person has
        /// started today.
        /// </summary>
        /// <remarks>
        /// Lower-cased, then without a <c>+tag</c>, and for Gmail without the dots Gmail ignores.
        /// Without this, <c>me+1@gmail.com</c> and <c>me+2@gmail.com</c> would be two organizers with
        /// two daily limits, and one inbox could invent as many as it liked.
        ///
        /// A key, never an address: nothing is sent to it. Invariant lower-casing, because the
        /// Turkish dotless i turns an ASCII 'I' into something that no longer matches.
        /// </remarks>
        public string ToMailboxKey()
        {
            var normalized = input.TrimNullSafe().ToLowerInvariant();

            var at = normalized.LastIndexOf('@');

            if(at <= 0)
                return normalized;

            var local = normalized[..at];
            var domain = normalized[(at + 1)..];

            var plus = local.IndexOf('+');

            if(plus > 0)
                local = local[..plus];

            if(domain is "gmail.com" or "googlemail.com")
            {
                local = local.Replace(".", string.Empty);
                domain = "gmail.com";
            }

            return string.IsNullOrEmpty(local)
                ? normalized
                : $"{local}@{domain}";
        }
    }
}
