using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace NCATAIBlazorFrontendTest.Shared
{
    public class DropboxUploadPayload
    {
        [JsonPropertyName("filePath")]
        public string FilePath { get; set; }
        [JsonPropertyName("fileName")]
        public string FileName { get; set; }
    }
}
