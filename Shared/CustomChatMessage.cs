using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NCATAIBlazorFrontendTest.Shared
{
    public class CustomChatMessage
    {
        public string Text { get; set; }
        public bool IsUser { get; set; }
        public string Context { get; set; }
        public bool ShowContext { get; set; } = false;
    }
}
