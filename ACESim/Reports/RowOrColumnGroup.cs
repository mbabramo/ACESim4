using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;

namespace ACESim
{
    [Serializable]
    public class RowOrColumnGroup
    {
        public string name;
        public List<RowOrColInfoOrGenerator> rowOrColOrGenerators;
        public List<RowOrColInfo> rowOrColsIncludingGenerated;
        
        public RowOrColumnGroup(string theName, List<RowOrColInfo> theRowOrColInfos, List<RowOrColInfoGenerator> theRowOrColInfoGenerators)
        {
            rowOrColOrGenerators = new List<RowOrColInfoOrGenerator>();
            foreach (var theRowOrColInfo in theRowOrColInfos)
                rowOrColOrGenerators.Add(theRowOrColInfo);
            foreach (var theRowOrColInfoGenerator in theRowOrColInfoGenerators)
                rowOrColOrGenerators.Add(theRowOrColInfoGenerator);
            name = theName;
        }

        public void Generate(List<GameProgressReportable> theOutputs)
        {
            rowOrColsIncludingGenerated = new List<RowOrColInfo>();
            foreach (var rowOrColOrGenerator in rowOrColOrGenerators)
            {
                if (rowOrColOrGenerator is RowOrColInfo)
                    rowOrColsIncludingGenerated.Add((RowOrColInfo)rowOrColOrGenerator);
                else if (rowOrColOrGenerator is RowOrColInfoGenerator && theOutputs.Count > 0)
                {
                    RowOrColInfoGenerator theGenerator = rowOrColOrGenerator as RowOrColInfoGenerator;
                    string theVariableName = ((RowOrColInfoGenerator)rowOrColOrGenerator).filterVariableName;
                    bool found;
                    Type theFieldType;
                    GameProgressReportable sampleOutput = theOutputs.FirstOrDefault(x => { var y = x.GetValueForReport(theVariableName, null, out found); return y != null; });
                    if (sampleOutput == null)
                    {
                        theFieldType = typeof(double?);
                    }
                    else
                    {
                        object sampleOutputValue = sampleOutput.GetValueForReport(theVariableName, null, out found);
                        if (!found)
                        {
                            sampleOutputValue = sampleOutput.GetValueForReport(theVariableName, null, out found); // allows tracing of error
                            throw new Exception("Report variable " + theVariableName + " was not included in the GameProgress results.");
                        }
                        theFieldType = sampleOutputValue.GetType();
                    }
                    List<RowOrColInfo> multipleToAdd = null;
                    if (theFieldType == typeof(double) || theFieldType == typeof(double?))
                    {
                        List<double?> theValues = theOutputs.Select(x => x.GetValueForReport(theVariableName, null, out found)).Cast<double?>().Distinct().ToList();
                        multipleToAdd = theGenerator.GenerateRowOrColInfos(theValues);
                    }
                    else if (theFieldType == typeof(int) || theFieldType == typeof(int?))
                    {
                        List<int> theValues = theOutputs.Select(x => x.GetValueForReport(theVariableName, null, out found)).Where(x => x != null).Cast<int>().OrderBy(y => y).Distinct().ToList();
                        multipleToAdd = theGenerator.GenerateRowOrColInfos(theValues);
                    }
                    else if (theFieldType == typeof(bool))
                    {
                        List<bool> theValues = theOutputs.Select(x => x.GetValueForReport(theVariableName, null, out found)).Cast<bool>().Distinct().ToList();
                        multipleToAdd = theGenerator.GenerateRowOrColInfos(theValues);
                    }
                    else if (theFieldType == typeof(string))
                    {
                        List<string> theValues = theOutputs.Select(x => x.GetValueForReport(theVariableName, null, out found)).Cast<string>().Distinct().ToList();
                        multipleToAdd = theGenerator.GenerateRowOrColInfos(theValues);
                    }
                    else
                        throw new Exception("Field of output iterations is of an unexpected type.");
                    rowOrColsIncludingGenerated.AddRange(multipleToAdd);
                }
            }
        }
    }
}
