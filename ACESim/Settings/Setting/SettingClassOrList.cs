using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Linq.Expressions;

namespace ACESim
{
    [Serializable]
    public abstract class SettingClassOrList : Setting
    {
        public List<Setting> ContainedSettings;
        public string CodeGeneratorName = "";
        public string CodeGeneratorOptions = "";
        public CodeBasedSettingGeneratorFactory CodeGeneratorFactory;
        public ICodeBasedSettingGenerator Generator;

        public SettingClassOrList(string name, Dictionary<string, double> allVariablesFromProgram, List<Setting> theContainedSettings, CodeBasedSettingGeneratorFactory codeGeneratorFactory, string codeGeneratorName, string codeGeneratorOptions)
            : base(name, allVariablesFromProgram)
        {
            ContainedSettings = theContainedSettings;
            CodeGeneratorName = codeGeneratorName ?? "";
            CodeGeneratorOptions = codeGeneratorOptions;
            CodeGeneratorFactory = codeGeneratorFactory; 
            if (codeGeneratorName != "")
                Generator = codeGeneratorFactory.GetCodeGenerator(codeGeneratorName);
            //if (codeGeneratorName != "")
            //    Generator = CodeBasedSettingGeneratorFactoryInstance.GetCodeGenerator(codeGeneratorName);
        }

        public override int GetNumSeedsRequired()
        {
            return ContainedSettings.Sum(x => x.GetNumSeedsRequired());
        }

        public override Type GetReturnType()
        {
            throw new Exception("Internal exception. Should not confirm the return type of class and list.");
        }

        public override void ReplaceContainedVariableSettingWithDouble(string variableName, double replacementValue)
        {
            int numContainedSettings = ContainedSettings.Count;
            for (int i = 0; i < numContainedSettings; i++)
            {
                Setting theSetting = ContainedSettings[i];
                if ((theSetting is SettingVariableFromSetting && ((SettingVariableFromSetting)theSetting).VariableName == variableName) || (theSetting is SettingVariableFromProgram && ((SettingVariableFromProgram)theSetting).VariableName == variableName))
                {
                    ContainedSettings[i] = new SettingDouble(variableName, AllVariablesFromProgram, replacementValue);
                }
                else 
                    theSetting.ReplaceContainedVariableSettingWithDouble(variableName, replacementValue);
            }
        }

        public override bool ContainsUnresolvedVariable()
        {
            return ContainedSettings.Any(x => x.ContainsUnresolvedVariable());
        }

        
        public override void SetVariableFromSettingTracker(SettingVariableFromSettingTracker variableFromSettingTracker)
        {
            base.SetVariableFromSettingTracker(variableFromSettingTracker);
            ContainedSettings.ForEach(x => x.SetVariableFromSettingTracker(variableFromSettingTracker));
        }


        public override void RequestVariableFromSettingValues(Dictionary<string, Setting> previousSettings, List<Setting> requestedSettings, ref int requestNumber)
        {
            foreach (var setting in ContainedSettings)
                setting.RequestVariableFromSettingValues(previousSettings, requestedSettings, ref requestNumber);
            base.RequestVariableFromSettingValues(previousSettings, requestedSettings, ref requestNumber);
        }
    }

    [Serializable]
    public class SettingList : SettingClassOrList
    {
        public SettingList(string name, Dictionary<string, double> allVariablesFromProgram, List<Setting> theContainedSettings, CodeBasedSettingGeneratorFactory codeGeneratorFactory, string codeGeneratorName, string codeGeneratorOptions)
            : base(name, allVariablesFromProgram, theContainedSettings, codeGeneratorFactory, codeGeneratorName, codeGeneratorOptions)
        {
            if (codeGeneratorName != null && codeGeneratorName != "")
                throw new NotImplementedException(); // we have not yet implemented generating a list in code (should not be too hard by following the code created for SettingClass
            Type = SettingType.List;
        }


        public override Setting DeepCopy()
        {
            return new SettingList(Name, AllVariablesFromProgram, ContainedSettings.Select(x => x.DeepCopy()).ToList(), CodeGeneratorFactory, CodeGeneratorName, CodeGeneratorOptions);
        }

        public override Expression GetExpressionForSetting(SettingCompilation compiler)
        {
            throw new Exception("Internal error: SettingCompilation should not call GetExpressionForSetting for a class or list.");
        }
    }

    [Serializable]
    public class SettingClass : SettingClassOrList
    {
        public Type SubclassType;

        public SettingClass(string name, Type subclassTypeToUse, Dictionary<string, double> allVariablesFromProgram, List<Setting> theContainedSettings, CodeBasedSettingGeneratorFactory codeGeneratorFactory, string codeGeneratorName, string codeGeneratorOptions)
            : base(name, allVariablesFromProgram, theContainedSettings, codeGeneratorFactory, codeGeneratorName, codeGeneratorOptions)
        {
            SubclassType = subclassTypeToUse;
            Type = SettingType.Class;
        }

        public override Setting DeepCopy()
        {
            return new SettingClass(Name, SubclassType, AllVariablesFromProgram, ContainedSettings.Select(x => x.DeepCopy()).ToList(), CodeGeneratorFactory, CodeGeneratorName, CodeGeneratorOptions);
        }

        public override Expression GetExpressionForSetting(SettingCompilation compiler)
        {
            throw new Exception("Internal error: SettingCompilation should not call GetExpressionForSetting for a class or list.");
        }
    }
}
