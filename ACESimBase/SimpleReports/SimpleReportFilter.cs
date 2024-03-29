﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESim
{
    [Serializable]
    public class SimpleReportFilter
    {
        public string Name;
        public Func<GameProgress, bool> IsInFilter;
        public string MultiplyByAllColumnForRowWithName;
        public string DivideByAllColumnForRowWithName;
        public bool UseSum;

        public override string ToString()
        {
            string s = Name;
            if (MultiplyByAllColumnForRowWithName != null)
                s += $" *{MultiplyByAllColumnForRowWithName}-All";
            if (DivideByAllColumnForRowWithName != null)
                s += $" /{DivideByAllColumnForRowWithName}-All";
            if (UseSum)
                s += " (Sum)";
            return s;
        }

        public SimpleReportFilter(string name, Func<GameProgress, bool> isInFilter)
        {
            Name = name;
            IsInFilter = isInFilter;
        }

        public SimpleReportFilter WithDivisionByAllColumnForRowWithSameName(bool divide = true)
        {
            if (divide)
                DivideByAllColumnForRowWithName = Name;
            return this;
        }

        internal double? Manipulate(Dictionary<string, double?> firstColumnValues, double? value)
        {
            if (MultiplyByAllColumnForRowWithName != null)
            {
                if (firstColumnValues.ContainsKey(MultiplyByAllColumnForRowWithName))
                    value = value * firstColumnValues[MultiplyByAllColumnForRowWithName];
            }
            if (DivideByAllColumnForRowWithName != null)
            {
                if (firstColumnValues.ContainsKey(DivideByAllColumnForRowWithName))
                    value = value / firstColumnValues[DivideByAllColumnForRowWithName];
            }
            return value;
        }
    }
}
