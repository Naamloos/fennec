using System;
using System.Collections.Generic;
using System.Text;

namespace Dev.Naamloos.Fennec.Sdk.Interfaces
{
    public interface IAsyncSecureStorage
    {
        public Task<string?> GetAsync(string key);
        public Task SetAsync(string key, string value);
        public Task RemoveAsync(string key);
    }
}
