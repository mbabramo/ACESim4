using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Linq.Expressions;

namespace ACESim
{
    public enum SettingCalcOperator
    {
        Plus,
        Minus,
        Times,
        Divide,
        Power,
        Curve
    }

    [Serializable]
    class SettingCalc : Setting
    {
        public static Dictionary<string, SettingCalcOperator> OperatorStringsToOperators;

        /// <summary>
        /// Initialize OperatorStringsToOperators
        /// </summary>
        static SettingCalc()
        {
            OperatorStringsToOperators = new Dictionary<string, SettingCalcOperator>();
            OperatorStringsToOperators.Add("+", SettingCalcOperator.Plus);
            OperatorStringsToOperators.Add("-", SettingCalcOperator.Minus);
            OperatorStringsToOperators.Add("*", SettingCalcOperator.Times);
            OperatorStringsToOperators.Add("/", SettingCalcOperator.Divide);
            OperatorStringsToOperators.Add("P", SettingCalcOperator.Power);
            OperatorStringsToOperators.Add("C", SettingCalcOperator.Curve);
        }

        public SettingCalcOperator Operator;
        List<Setting> Operands;

        public SettingCalc(string name, Dictionary<string, double> allVariablesFromProgram, SettingCalcOperator oper8or, IEnumerable<Setting> operands)
            : base(name, allVariablesFromProgram)
        {
            // To avoid confusion, require more than 1 operand, even though the calculations below support them.
            if (operands.Count() < 2)
                throw new ArgumentException("SettingCalc does not allow unary operations in order to avoid confusion.");
            if (operands.Any(o => o == null))
                throw new ArgumentException("All operands to SettingCalc must be non-null.");

            SettingType[] numericSettingTypes = new SettingType[] {
                SettingType.Double,
                SettingType.Int32,
                SettingType.Distribution,
                SettingType.VariableFromSetting,
                SettingType.VariableFromProgram,
                SettingType.Calc
            };
            if (!operands.All(o => numericSettingTypes.Contains(o.Type)))
                throw new ArgumentException(String.Format(
                    "SettingCalc currently only supports as operands numeric settings or settings that evaluate to numbers.  ({0})",
                    String.Join(", ", numericSettingTypes.Select(s => s.ToString()))));

            //switch (leftOperand.Type)
            //{
            //    case SettingType.Single:
            //    case SettingType.Int32:
            //    case SettingType.Distribution:
            //    case SettingType.Variable:
            //        switch (rightOperand.Type)
            //        {
            //            case SettingType.Single:
            //            case SettingType.Int32:
            //            case SettingType.Distribution:
            //            case SettingType.Variable:
            //                // Okay
            //                break;
            //            default:
            //                throw new ArgumentException("SettingCalc currently only supports operands of numeric settings or settings that evaluate to numbers.");
            //        }
            //        break;
            //    // In any of the non-numeric cases, would have to modify entire settings system to use more than floats.  I.e., GetFloatValue is the only way to get a value from a setting.
            //    case SettingType.String:
            //        // Possible could allow two strings and + to concatenate two strings.
            //        // Possibly could allow String * Integer to concatenate string that many times.  Would have to check operator too, then.
            //    case SettingType.Boolean:
            //        // Possibly could allow two booleans with + being logical OR and * being logical AND
            //    case SettingType.List:
            //        // Possible could allow two Lists and + to append one list to another.
            //        // Possibly could allow List, Int32, and * to add the list to itself n-1 times.
            //    default:
            //        throw new ArgumentException("SettingCalc currently only supports operands of numeric settings or settings that evaluate to numbers.");
            //}

            Operator = oper8or;
            Operands = operands.ToList();
            Type = SettingType.Calc;

            //if (!ContainsUnresolvedVariable())
            //{
            //    double initialValue = (double) GetValue(null);
            //    VariableFromSettingTracker.StoreSettingVariableFromSetting(initialValue);
            //}
        }

        public override Setting DeepCopy()
        {
            return new SettingCalc(Name, AllVariablesFromProgram, Operator, Operands.Select(x => x.DeepCopy()).ToList());
        }

        public override int GetNumSeedsRequired()
        {
            return Operands.Sum(o => o.GetNumSeedsRequired()); // .Aggregate(0, (sum, num) => sum + num);
        }

        public override Expression GetExpressionForSetting(SettingCompilation compiler)
        {
            bool returnTypeIsInt = Operands.All(o => o.GetReturnType() == typeof(int));
            Type returnType = returnTypeIsInt ? typeof(int) : typeof(double);
            Expression theExpression = GetExpressionForSettingHelper(returnTypeIsInt, compiler);
            Expression newNamedVariable = compiler.GetNewNamedVariable(returnType, theExpression, Name);
            return newNamedVariable;
        }

        public Expression GetExpressionForSettingHelper(bool returnTypeIsInt, SettingCompilation compiler)
        {
            List<Expression> operandExpressions = Operands.Select(x => x.GetExpressionForSetting(compiler)).ToList();

            switch (Operator)
            {
                case SettingCalcOperator.Plus:
                    Expression theExpress = operandExpressions[0];
                    for (int i = 1; i < operandExpressions.Count; i++)
                        theExpress = Expression.Add(theExpress, operandExpressions[i]);
                    return theExpress;
                case SettingCalcOperator.Minus:
                    theExpress = operandExpressions[0];
                    for (int i = 1; i < operandExpressions.Count; i++)
                        theExpress = Expression.Subtract(theExpress, operandExpressions[i]);
                    return theExpress;
                case SettingCalcOperator.Times:
                    theExpress = operandExpressions[0];
                    for (int i = 1; i < operandExpressions.Count; i++)
                        theExpress = Expression.Multiply(theExpress, operandExpressions[i]);
                    return theExpress;
                case SettingCalcOperator.Divide:
                    theExpress = operandExpressions[0];
                    for (int i = 1; i < operandExpressions.Count; i++)
                        theExpress = Expression.Divide(theExpress, operandExpressions[i]);
                    if (returnTypeIsInt)
                        return Expression.Convert(theExpress, typeof(int));
                    return theExpress;
                case SettingCalcOperator.Power:
                    Expression first = Expression.Convert(operandExpressions[0], typeof(double));
                    Expression second = Expression.Convert(operandExpressions[1], typeof(double));
                    Expression<Func<double,double,double>> powDelegate = (x,y) => Math.Pow(x,y);
                    Expression invoked = Expression.Invoke(powDelegate, new Expression[] { first, second });
                    if (returnTypeIsInt)
                        return Expression.Convert(invoked, typeof(int));
                    return invoked;
                case SettingCalcOperator.Curve:
                    Expression fromVal = Expression.Convert(operandExpressions[0], typeof(double));
                    Expression toVal  = Expression.Convert(operandExpressions[1], typeof(double));
                    Expression curvature = Expression.Divide(Expression.Constant(1.0), Expression.Convert(operandExpressions[2], typeof(double))); // note that we're taking the inverse of the specified curvature here
                    Expression proportion = Expression.Convert(operandExpressions[3], typeof(double));
                    powDelegate = (x,y) => Math.Pow(x,y);
                    Expression adjustedProportion = Expression.Invoke(powDelegate, new Expression[] { proportion, curvature });
                    Expression calcResult = Expression.Add(fromVal, Expression.Multiply(Expression.Subtract(toVal, fromVal), adjustedProportion));
                    if (returnTypeIsInt)
                        return Expression.Convert(calcResult, typeof(int));
                    return calcResult;
                default:
                    throw new Exception("Exceeded possible SettingCalc Operator types (this should not be.)");
            }
        }


        internal object GetValue(List<double> inputs, CurrentExecutionInformation settings)
        {
            double? overrideValue = GetOverrideValue(inputs, settings);
            if (overrideValue != null)
                return (double)overrideValue;

            object theValue;
            if (Operands.All(o => o.GetReturnType() == typeof(int))) // If SettingVariable is ever modified to return Ints, should also check for that here.
            {
                IEnumerable<int> operandsInts = 
                    Operands.Select(o => o.Type == SettingType.Calc ? (int)(o as SettingCalc).GetValue(inputs, settings) : (o as SettingInt32).Value);
                switch (Operator)
                {
                    case SettingCalcOperator.Plus:
                        theValue = operandsInts.Aggregate(0, (sum, i) => sum + i);
                        break;
                    case SettingCalcOperator.Minus:
                        if (operandsInts.Count() == 1)
                            // Interpret unary subtraction as negation (could also be identity)
                            theValue = -1 * operandsInts.First();
                        else
                            theValue = operandsInts.Take(1).Aggregate(operandsInts.First(), (sum, i) => sum - i);
                        break;
                    case SettingCalcOperator.Times:
                        theValue = operandsInts.Aggregate(1, (prod, i) => prod * i);
                        break;

                    case SettingCalcOperator.Divide:
                        if (operandsInts.Count() == 1)
                            // Interpret unary division as identity (could also be inversion, but integer arithmetic would imply that the value would be either ±1, 0, or ±undefined.)
                            theValue = operandsInts.First();
                        else
                            theValue = operandsInts.Skip(1).Aggregate(operandsInts.First(), (prod, i) => prod / i);
                        break;
                    case SettingCalcOperator.Power:
                        int theNum = operandsInts.First();
                        int thePower = operandsInts.Last();
                        theValue = (int) Math.Pow((double)theNum, (double)thePower);
                        break;
                    case SettingCalcOperator.Curve:
                        double fromVal = (double)operandsInts.First();
                        double toVal = (double)operandsInts.Skip(1).First();
                        double curvature = (double)operandsInts.Skip(2).First();
                        double proportion = (double)operandsInts.Skip(3).First();
                        
                        if (curvature <= 0)
                            throw new Exception("Invalid input to a curve distribution. Curvature must be greater than zero.");
                        if (proportion < 0 || proportion > 1)
                            throw new Exception("Invalid input to a uniform distribution. Must be between zero and one.");
                        double adjustedProportion = (double)Math.Pow(proportion, 1.0 / curvature);
                        theValue = (int) ( fromVal + (toVal - fromVal) * adjustedProportion );
                        break;
                    default:
                        throw new Exception("Exceeded possible SettingCalc Operator types (this should not be.)");
                }
            }
            else
            {
                IEnumerable<double> operandsDoubles =
                    Operands.Select(o => o.Type == SettingType.Calc ? (double)(o as SettingCalc).GetValue(inputs, settings) : o.GetDoubleValue(inputs));
                switch (Operator)
                {
                    case SettingCalcOperator.Plus:
                        theValue = operandsDoubles.Aggregate(0.0, (sum, i) => sum + i);
                        break;
                    case SettingCalcOperator.Minus:
                        if (operandsDoubles.Count() == 1)
                            // Interpret unary subtraction as negation (could also be identity)
                            theValue = -1.0 * operandsDoubles.First();
                        else
                            theValue = operandsDoubles.Skip(1).Aggregate(operandsDoubles.First(), (sum, i) => sum - i);
                        break;
                    case SettingCalcOperator.Times:
                        theValue = operandsDoubles.Aggregate(1.0, (prod, i) => prod * i);
                        break;
                    case SettingCalcOperator.Divide:
                        if (operandsDoubles.Count() == 1)
                            // Interpret unary division as identity (could also be inversion: 1.0 / first.)
                            theValue = operandsDoubles.First();
                        else
                            theValue = operandsDoubles.Skip(1).Aggregate(operandsDoubles.First(), (prod, i) => prod / i);
                        break;
                    case SettingCalcOperator.Power:
                        double theNum = operandsDoubles.First();
                        double thePower = operandsDoubles.Last();
                        theValue = Math.Pow(theNum, thePower);
                        break;
                    case SettingCalcOperator.Curve:
                        double fromVal = operandsDoubles.First();
                        double toVal = operandsDoubles.Skip(1).First();
                        double curvature = operandsDoubles.Skip(2).First();
                        double proportion = operandsDoubles.Skip(3).First();

                        if (curvature <= 0)
                            throw new Exception("Invalid input to a curve distribution. Curvature must be greater than zero.");
                        if (proportion < 0 || proportion > 1)
                            throw new Exception("Invalid input to a uniform distribution. Must be between zero and one.");
                        double adjustedProportion = (double)Math.Pow(proportion, curvature);
                        theValue = (fromVal + (toVal - fromVal) * adjustedProportion);
                        break;
                    default:
                        throw new Exception("Exceeded possible SettingCalc Operator types (this should not be.)");
                }
            }
            if (VariableFromSettingTracker.Value != null && ValueIsRequestedAsVariableFromSetting)
                VariableFromSettingTracker.Value.StoreSettingVariableFromSetting(ValueFromSettingRequestedOrder[0], (double)theValue);
            return theValue;
        }

        public override Type GetReturnType()
        {
            if (
                Operands.Where(o => o.Type == SettingType.Calc).All(o => o.GetReturnType() == typeof(int)) &&
                Operands.Where(o => o.Type != SettingType.Calc).All(o => o.Type == SettingType.Int32)
                )
                return typeof(int);
            else
                return typeof(double);
        }

        public override bool ContainsUnresolvedVariable()
        {
            return Operands.Any(x => x.ContainsUnresolvedVariable());
        }


        public override void SetVariableFromSettingTracker(SettingVariableFromSettingTracker variableFromSettingTracker)
        {
            base.SetVariableFromSettingTracker(variableFromSettingTracker);
            Operands.ForEach(x => x.SetVariableFromSettingTracker(variableFromSettingTracker));
        }

        public override void RequestVariableFromSettingValues(Dictionary<string, Setting> previousSettings, List<Setting> requestedSettings, ref int requestNumber)
        {
            foreach (var operand in Operands)
                operand.RequestVariableFromSettingValues(previousSettings, requestedSettings, ref requestNumber);
            base.RequestVariableFromSettingValues(previousSettings, requestedSettings, ref requestNumber);
        }

        public override void ReplaceContainedVariableSettingWithDouble(string variableName, double replacementValue)
        {
            int numContainedSettings = Operands.Count;
            for (int i = 0; i < numContainedSettings; i++)
            {
                Setting theSetting = Operands[i];
                if ((theSetting is SettingVariableFromSetting && ((SettingVariableFromSetting)theSetting).VariableName == variableName) || (theSetting is SettingVariableFromProgram && ((SettingVariableFromProgram)theSetting).VariableName == variableName))
                {
                    Operands[i] = new SettingDouble(variableName, AllVariablesFromProgram, replacementValue);
                }
                else
                    theSetting.ReplaceContainedVariableSettingWithDouble(variableName, replacementValue);
            }
        }
    }
}
