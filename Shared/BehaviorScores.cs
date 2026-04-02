using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NCATAIBlazorFrontendTest.Shared
{
    public class BehaviorScores
    {
        public double ConfusionScore { get; set; }
        public double HesitationScore { get; set; }
        public double ImpulsivityScore { get; set; }
        public double HintDependenceScore { get; set; }

        public string? PredictedState { get; set; }
    }
}
