using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NCATAIBlazorFrontendTest.Shared
{
    public class GptExplanationResult
    {
        public string LearnerStateSummary { get; set; } = "";
        public string WhySupportChanged { get; set; } = "";
        public string CoachMessage { get; set; } = "";
        public string ConfidenceNote { get; set; } = "";
    }
}
