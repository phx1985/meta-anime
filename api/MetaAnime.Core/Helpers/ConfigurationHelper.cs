namespace MetaAnime.Core.Helpers
{
    public static class ConfigurationHelper
    {
        /// <summary>
        /// Get environment variable or throw exception
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static string GetEnvironmentVariableOrThrow(string key)
            => Environment.GetEnvironmentVariable(key) ??
               throw new ArgumentNullException(key, $"{key} Environment Variable is not set");
    }
}
