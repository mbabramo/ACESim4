//------------------------------------------------------------------------------
// <auto-generated>
//    This code was generated from a template.
//
//    Manual changes to this file may cause unexpected behavior in your application.
//    Manual changes to this file will be overwritten if the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

namespace ACESim.Reports
{
    using System;
    using System.Collections.Generic;
    
    public partial class ExecutionResultSet
    {
        public ExecutionResultSet()
        {
            this.DataPoints = new HashSet<DataPoint>();
            this.SettingChoiceToExecutionResultSets = new HashSet<SettingChoiceToExecutionResultSet>();
        }
    
        public int Id { get; set; }
        public byte[] RowVersion { get; set; }
        public System.DateTime Time { get; set; }
        public string SettingChoiceSummary { get; set; }
        public string FullSettingsList { get; set; }
        public string FullVariableList { get; set; }
    
        public virtual ICollection<DataPoint> DataPoints { get; set; }
        public virtual ICollection<SettingChoiceToExecutionResultSet> SettingChoiceToExecutionResultSets { get; set; }
    }
}
