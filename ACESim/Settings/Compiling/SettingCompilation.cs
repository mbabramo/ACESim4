using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Linq.Expressions;
using System.Reflection;
using System.Collections;

namespace ACESim
{
    public class SettingCompilation // <T> where T : object
    {
        public int NumInputArrayAccess = 0;
        public int LastInputArrayAccessProcessedForNewNamedVariable = -1;
        public int NumAssignments = 0;

        ParameterExpression inputsArray = Expression.Parameter(typeof(double[]), "inputsArray");
        ParameterExpression programSettings = Expression.Parameter(typeof(Dictionary<string, double>), "settingsFromProgram");
        Expression variableToReturn = null;

        List<ParameterExpression> variablesToAssignTo = new List<ParameterExpression>();
        List<string> variableFromSettingsNames = new List<string>();
        List<Expression> assignmentStatements = new List<Expression>();

        // Keep track of whether we should swap two inputs with each other on odd iterations, or alternative flip an input (i.e., set it to 1 - value).
        List<string> OddIterationSwapInputSeedsStrings = new List<string>();
        List<bool> OddIterationFlipInputSeed = new List<bool>();

        public Expression GetNewNamedVariable(Type type, Expression expressionToAssignToNewNamedVariable, string variableFromSettingName, FieldInfo field = null)
        {
            string variableToAssignToName = "var" + NumAssignments.ToString();
            RecordInformationOnOddIterationInputManipulations(field);
            ParameterExpression newVariableToAssignTo = Expression.Parameter(type, variableToAssignToName);
            variablesToAssignTo.Add(newVariableToAssignTo);
            variableFromSettingsNames.Add(variableFromSettingName);
            Expression assignmentStatement = Expression.Assign(newVariableToAssignTo, expressionToAssignToNewNamedVariable);
            assignmentStatements.Add(assignmentStatement);
            NumAssignments++;
            if (variableToReturn == null)
                variableToReturn = newVariableToAssignTo; // the first variable that we name will be what we eventually return
            return newVariableToAssignTo;
        }

        private void ProcessCompletedOddIterationInputManipulations(out bool[] flipSeed, out int?[] substituteSeed)
        {
            int length = OddIterationSwapInputSeedsStrings.Count();
            if (length == 0)
            {
                flipSeed = null;
                substituteSeed = null;
                return;
            }
            flipSeed = OddIterationFlipInputSeed.ToArray();
            substituteSeed = new int?[length];
            for (int i = 0; i < length; i++)
            {
                string swapString = OddIterationSwapInputSeedsStrings[i];
                OddIterationSwapInputSeedsStrings[i] = "Was: " + swapString;
                if (swapString != null)
                {
                    List<int> matchingIndices = OddIterationSwapInputSeedsStrings.Select((item, index) => new { Item = item, Index = index }).Where(x => x.Item == swapString && x.Index != i).Select(x => x.Index).ToList();
                    int nextMatch = matchingIndices.First();
                    substituteSeed[i] = nextMatch;
                    substituteSeed[nextMatch] = i;
                    OddIterationSwapInputSeedsStrings[nextMatch] = "Was: " + swapString;
                }
            }
        }

        private void RecordInformationOnOddIterationInputManipulations(FieldInfo field)
        {
            if (NumInputArrayAccess > 0 && NumInputArrayAccess > LastInputArrayAccessProcessedForNewNamedVariable + 1)
            { // we have processed another input array access
                int previousLastInputArrayAccessProcessed = LastInputArrayAccessProcessedForNewNamedVariable;
                LastInputArrayAccessProcessedForNewNamedVariable = NumInputArrayAccess - 1;
                int numInputsForThisNamedVariable = LastInputArrayAccessProcessedForNewNamedVariable - previousLastInputArrayAccessProcessed;
                for (int i = 0; i < numInputsForThisNamedVariable; i++)
                {
                    // System.Diagnostics.Debug.WriteLine("PROCESSING " + LastInputArrayAccessProcessedForNewNamedVariable + " " + (field == null ? "" : field.Name));
                    bool swapDefined = false;
                    if (field != null && Attribute.IsDefined(field, typeof(SwapInputSeedsAttribute)))
                    {
                        swapDefined = true;
                        SwapInputSeedsAttribute swapAttr = (SwapInputSeedsAttribute)Attribute.GetCustomAttribute(field, typeof(SwapInputSeedsAttribute));
                        if (swapAttr.Name == "" || swapAttr.Name == null)
                            throw new Exception();
                        OddIterationSwapInputSeedsStrings.Add(swapAttr.Name);
                    }
                    else
                        OddIterationSwapInputSeedsStrings.Add(null);
                    if (field != null && Attribute.IsDefined(field, typeof(FlipInputSeedAttribute)))
                    {
                        //if (swapDefined)
                        //    throw new Exception("Cannot declare swap input seed and flip input seed attributes together.");
                        OddIterationFlipInputSeed.Add(true);
                    }
                    else
                        OddIterationFlipInputSeed.Add(false);
                }
            }
        }

        public Expression GetNewInstanceOfTypeUsingParameterlessConstructor(Type type)
        {
            var constructorCallExpression = Expression.New(type);
            return constructorCallExpression;
        }

        public MethodCallExpression GetCallExpression<T>(Expression<Func<T>> e)
        {
            return e.Body as MethodCallExpression;
        }

        public MethodCallExpression GetCallExpression<T, U>(Expression<Func<T, U>> e)
        {
            return e.Body as MethodCallExpression;
        }

        public MethodCallExpression GetCallExpression<T, U, V>(Expression<Func<T, U, V>> e)
        {
            return e.Body as MethodCallExpression;
        }

        public MethodCallExpression GetCallExpressionAction<T, U>(Expression<Action<T, U>> e)
        {
            return e.Body as MethodCallExpression;
        }

        public void AssignExpressionToField(Expression variableContainingFieldToBeAssignedTo, FieldInfo field, int? arrayIndexInFieldIfAny, Expression expressionToAssign)
        {
            MemberExpression fieldExp = Expression.Field(variableContainingFieldToBeAssignedTo, field);
            Expression fieldOrIndexInFieldExp;
            if (arrayIndexInFieldIfAny == null)
                fieldOrIndexInFieldExp = fieldExp;
            else
                fieldOrIndexInFieldExp = Expression.ArrayIndex(fieldExp, Expression.Constant((int)arrayIndexInFieldIfAny));
            BinaryExpression assignExp = Expression.Assign(fieldOrIndexInFieldExp, expressionToAssign);
            assignmentStatements.Add(assignExp);
        }

        public Expression GetExpressionForInputArrayIndex(int inputIndex)
        {
            Expression theExpression = Expression.ArrayIndex(inputsArray, Expression.Constant(inputIndex)); // inputsArray is a one dimensional array, otherwise we would pass an array of Expressions as the second part
            return theExpression;
        }

        public Expression GetExpressionForNextInputArrayIndex()
        {
            Expression theExpression = GetExpressionForInputArrayIndex(NumInputArrayAccess);
            NumInputArrayAccess++;
            return theExpression;
        }

        public Expression GetExpressionForVariableFromProgram(string variableFromProgram)
        {
            Expression<Func<Dictionary<string, double>, string, double>> dictionaryAccessExpression = (theDict, theString) => theDict[theString]; // first, we create a delegate (so that we don't have to call TryGetValue directly)
            Expression invoked = Expression.Invoke(dictionaryAccessExpression, programSettings, Expression.Constant(variableFromProgram)); // now, we invoke the delegate, returning an expression
            return invoked;
        }

        public Expression GetExpressionForVariableFromSetting(string variableFromSetting)
        {
            int indexWithinVariablesCreated = variableFromSettingsNames.Select((x, i) => new { Item = x, Index = i }).Last(y => y.Item == variableFromSetting).Index;
            Expression variableAccessExpression = variablesToAssignTo[indexWithinVariablesCreated];
            return variableAccessExpression;
        }

        public Func<double[], Dictionary<string, double>, object> GetCompiledExpression()
        {
            BlockExpression block = Expression.Block(typeof(object), 
                variablesToAssignTo, // variables
                assignmentStatements // statements
                .Concat(new List<Expression> { Expression.Convert(variableToReturn, typeof(object)) } // the variable to return
                )); 
            Func<double[], Dictionary<string, double>, object> compiledExpression = Expression.Lambda<Func<double[], Dictionary<string, double>, object>>(block, inputsArray, programSettings).Compile();
            return compiledExpression;
        }

        public Func<double[], Dictionary<string, double>, object> GetCompiledExpressionFromSettingAndFieldInfos(Type outerType, List<SettingAndFieldInfo> theSettingAndFieldInfos, string name, out bool[] flipSeed, out int?[] substituteSeed)
        {
            ProcessOuterSettingAndFieldInfo(outerType, theSettingAndFieldInfos, name);
            ProcessCompletedOddIterationInputManipulations(out flipSeed, out substituteSeed);
            return GetCompiledExpression();
        }

        private Expression ProcessOuterSettingAndFieldInfo(Type outerType, List<SettingAndFieldInfo> theSettingAndFieldInfos, string name)
        {
            Expression overallObject = GetNewInstanceOfTypeUsingParameterlessConstructor(outerType);
            Expression variableRepresentingOverallObject = GetNewNamedVariable(outerType, overallObject, name);
            ProcessInnerSettingAndFieldInfos(variableRepresentingOverallObject, theSettingAndFieldInfos, false);
            return variableRepresentingOverallObject;
        }

        private void ProcessInnerSettingAndFieldInfos(Expression outerObject, List<SettingAndFieldInfo> innerSettingAndFieldInfos, bool isListRatherThanClass)
        {
            int itemNum = 0;
            foreach (SettingAndFieldInfo innerSettingAndFieldInfo in innerSettingAndFieldInfos)
            {
                Expression innerExpression = GetExpressionForSettingAndFieldInfo(innerSettingAndFieldInfo);
                Expression variableRepresentingInnerObject = GetNewNamedVariable(innerSettingAndFieldInfo.type, innerExpression, innerSettingAndFieldInfo.setting.Name, innerSettingAndFieldInfo.fieldInfo);
                if (isListRatherThanClass)
                {
                    Type type = typeof(IList);
                    MethodInfo methodInfo = type.GetMethod("Add");
                    MethodCallExpression methodExpression = Expression.Call(outerObject, methodInfo, Expression.Convert(variableRepresentingInnerObject, typeof(object)));
                    //MethodCallExpression methodExpression = GetCallExpressionAction<IList, object>((x, y) => x.Add(y));
                    assignmentStatements.Add(methodExpression); // here we need to add the variableRepresentingInnerObject to the outerObject (which is a List).
                }
                else
                    AssignExpressionToField(outerObject, innerSettingAndFieldInfo.fieldInfo, null, variableRepresentingInnerObject);
                itemNum++;
            }
        }

        public Expression GetExpressionForSettingAndFieldInfo(SettingAndFieldInfo theSettingAndFieldInfo)
        {
            switch (theSettingAndFieldInfo.setting.Type)
            {
                case SettingType.List:
                    // This must return an expression that is a list containing all of the innerSettingAndFieldInfos
                    Expression outerObject = GetNewInstanceOfTypeUsingParameterlessConstructor(theSettingAndFieldInfo.type); // should create the generic list
                    List<SettingAndFieldInfo> innerSettingAndFieldInfos = theSettingAndFieldInfo.GetContainedSettingAndFieldInfos(theSettingAndFieldInfo.type.GetGenericArguments()[0]);
                    //Expression debugExpression = ProcessOuterSettingAndFieldInfo(theSettingAndFieldInfo.type, innerSettingAndFieldInfos, theSettingAndFieldInfo.setting.Name);
                    //return debugExpression;
                    Expression variableRepresentingOverallObject = GetNewNamedVariable(theSettingAndFieldInfo.type, outerObject, theSettingAndFieldInfo.fieldInfo.Name);
                    ProcessInnerSettingAndFieldInfos(variableRepresentingOverallObject, innerSettingAndFieldInfos, isListRatherThanClass: true);
                    return variableRepresentingOverallObject;

                case SettingType.Class:
                    Expression theExpression;
                    ICodeBasedSettingGenerator generator = ((SettingClass)theSettingAndFieldInfo.setting).Generator;
                    if (generator != null)
                    {
                        Expression newInstanceOfGenerator = GetNewInstanceOfTypeUsingParameterlessConstructor(generator.GetType());
                        Expression optionsArgument = Expression.Constant(((SettingClass)theSettingAndFieldInfo.setting).CodeGeneratorOptions);
                        Expression callExpression = Expression.Call(newInstanceOfGenerator, generator.GetType().GetMethod("GenerateSetting"), optionsArgument); 
                        Expression castToObject = Expression.Convert(callExpression, theSettingAndFieldInfo.type);
                        theExpression = castToObject;
                    }
                    else
                        theExpression = ProcessOuterSettingAndFieldInfo(theSettingAndFieldInfo.type, theSettingAndFieldInfo.GetContainedSettingAndFieldInfos(theSettingAndFieldInfo.type), theSettingAndFieldInfo.setting.Name);
                    return theExpression;

                case SettingType.Distribution:
                case SettingType.Double:
                case SettingType.VariableFromProgram:
                case SettingType.VariableFromSetting:
                case SettingType.Boolean:
                case SettingType.String:
                case SettingType.Int32:
                case SettingType.Int64:
                case SettingType.Calc:
                    Expression theExpressionForSetting = theSettingAndFieldInfo.setting.GetExpressionForSetting(this);
                    return theExpressionForSetting;

                case SettingType.Strategy:
                    throw new NotImplementedException("We have not yet implemented compilation using strategy as a game input.");

                default:
                    throw new Exception(String.Format("Exhausted SettingTypes ({0})", theSettingAndFieldInfo.setting.Type));
            }
        }

        public Expression Sum(Expression[] expressionsToSum)
        {
            Expression sumExpression = expressionsToSum[0];
            for (int i = 1; i < expressionsToSum.Length; i++)
                sumExpression = Expression.Add(sumExpression, expressionsToSum[i]);
            return sumExpression;
        }

        public Expression Average(Expression[] expressionsToAverage)
        {
            return Expression.Divide(Sum(expressionsToAverage), Expression.Constant((double)expressionsToAverage.Length));
        }

    }
}
