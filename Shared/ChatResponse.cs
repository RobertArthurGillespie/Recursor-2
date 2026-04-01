using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NCATAIBlazorFrontendTest.Shared
{
    public class ChatResponse
    {
        public string Answer { get; set; }
        public string Context { get; set; } // Optional: to show the user what context was used
    }
}
