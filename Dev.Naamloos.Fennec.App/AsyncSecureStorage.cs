using Dev.Naamloos.Fennec.Sdk.Interfaces;
using System;
using System.Collections.Generic;
using System.Text;

namespace Dev.Naamloos.Fennec.App
{
    public class AsyncSecureStorage : IAsyncSecureStorage
    {
        public Task<string?> GetAsync(string key)
        {
            return SecureStorage.Default.GetAsync(key);
        }

        public Task RemoveAsync(string key)
        {
            SecureStorage.Default.Remove(key);
            return Task.CompletedTask;
        }

        public Task SetAsync(string key, string value)
        {
            return SecureStorage.Default.SetAsync(key, value);
        }
    }
}
