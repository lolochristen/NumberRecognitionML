using Microsoft.ML;
using Microsoft.ML.Data;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Packaging;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NumberRecognitionML
{
    /// <summary>
    /// The Digit class represents one mnist digit.
    /// </summary>
    public class Digit
    {
        public float Number;
        [VectorType(784)] public float[] PixelValues;
    }

    /// <summary>
    /// The DigitPrediction class represents one digit prediction.
    /// </summary>
    public class DigitPrediction
    {
        public float[] Score;
    }

    public class NumberRecognition
    {
        private static bool HasHeaders = true;
        public void Train(string dataPath, string modelPath)
        {
            // create a machine learning context
            var context = new MLContext();

            // load data
            Trace.WriteLine("Loading data....");
            var dataView = context.Data.LoadFromTextFile(
                path: dataPath,
                columns: new[]
                {
                    new TextLoader.Column("Number", DataKind.Single, 0),
                    new TextLoader.Column(nameof(Digit.PixelValues), DataKind.Single, 1, 784)
                },
                hasHeader: HasHeaders,
                separatorChar: ',');

            // split data into a training and test set
            var partitions = context.Data.TrainTestSplit(dataView, testFraction: 0.2);

            // build a training pipeline
            // step 1: concatenate all feature columns
            var pipeline = context.Transforms.Concatenate("Features",
                //DefaultColumnNames.Features,
                nameof(Digit.PixelValues))

                .Append(context.Transforms.Conversion.MapValueToKey(inputColumnName: "Number", outputColumnName: "Label"))

                // step 2: cache data to speed up training                
                .AppendCacheCheckpoint(context)

                // step 3: train the model with SDCA
                .Append(context.MulticlassClassification.Trainers.SdcaMaximumEntropy(
                    labelColumnName: "Label",
                    featureColumnName: "Features"));

            // train the model
            Trace.WriteLine("Training model....");
            var model = pipeline.Fit(partitions.TrainSet);


            // use the model to make predictions on the test data
            Trace.WriteLine("Evaluating model....");
            var predictions = model.Transform(partitions.TestSet);

            // evaluate the predictions
            var metrics = context.MulticlassClassification.Evaluate(
                data: predictions
                //label: "Number",
                //score: "Score" /*DefaultColumnNames.Score*/
                );

            // show evaluation metrics
            Trace.WriteLine($"Evaluation metrics");
            Trace.WriteLine($"    MicroAccuracy:    {metrics.MicroAccuracy:0.###}");
            Trace.WriteLine($"    MacroAccuracy:    {metrics.MacroAccuracy:0.###}");
            Trace.WriteLine($"    LogLoss:          {metrics.LogLoss:#.###}");
            Trace.WriteLine($"    LogLossReduction: {metrics.LogLossReduction:#.###}");

            context.Model.Save(model, dataView.Schema, modelPath);
            Trace.WriteLine($"Model {modelPath} saved.");

            _predictionEngine = context.Model.CreatePredictionEngine<Digit, DigitPrediction>(model);

            //// grab three digits from the data: 2, 7, and 9
            //var digits = context.Data.CreateEnumerable<Digit>(dataView, reuseRowObject: false).ToArray();
            //var testDigits = new Digit[] { digits[5], digits[12], digits[20] };

            //// create a prediction engine
            //var engine = context.Model.CreatePredictionEngine<Digit, DigitPrediction>(model);
            ////var engine = model.CreatePredictionEngine<Digit, DigitPrediction>(context);

            //// predict each test digit
            //for (var i = 0; i < testDigits.Length; i++)
            //{
            //    var prediction = engine.Predict(testDigits[i]);

            //    // show results
            //    Console.WriteLine($"Predicting test digit {i}...");
            //    for (var j = 0; j < 10; j++)
            //    {
            //        Console.WriteLine($"  {j}: {prediction.Score[j]:P2}");
            //    }
            //    Console.WriteLine();
            //}
        }

        private PredictionEngine<Digit, DigitPrediction> _predictionEngine;

        public void LoadModel(string modelPath)
        {
            var context = new MLContext();
            DataViewSchema schema;
            var model = context.Model.Load(modelPath, out schema);
            _predictionEngine = context.Model.CreatePredictionEngine<Digit, DigitPrediction>(model);
            Trace.WriteLine($"Model {modelPath} loaded.");
        }

        public DigitPrediction PredictDigit(Digit digit)
        {
            try
            {
                return _predictionEngine.Predict(digit);
            }
            catch(Exception exp)
            {
                Trace.WriteLine("Prediction failed. Model loaded?");
                return null;
            }
        }

        public char PredictDigitEvaluate(Digit digit, out float score)
        {
            var digitPred = _predictionEngine.Predict(digit);
            score = digitPred.Score.Max();
            for (int i = 0; i < digitPred.Score.Length; i++)
                if (digitPred.Score[i] == score)
                    return (char) (i + 48);
            return ' ';
        }
    }
}
