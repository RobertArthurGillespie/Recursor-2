using Microsoft.ML.Data;

namespace NCATAIBlazorFrontendTest.Server.Recursor.ML
{
    public class NextWindowPredictionRow
    {
        [ColumnName("LabelHintDependenceNext")]
        public bool LabelHintDependenceNext { get; set; }

        [ColumnName("PredictedLabel")]
        public bool PredictedLabel { get; set; }

        [ColumnName("Probability")]
        public float Probability { get; set; }

        [ColumnName("Score")]
        public float Score { get; set; }

        public string SessionId { get; set; } = "";
        public float WindowIndex { get; set; }
    }
}
