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
    
    public partial class SettingChoice
    {
        public SettingChoice()
        {
            this.SettingChoiceToExecutionResultSets = new HashSet<SettingChoiceToExecutionResultSet>();
            this.SettingChoiceToSettingChoiceSets = new HashSet<SettingChoiceToSettingChoiceSet>();
        }
    
        public int Id { get; set; }
        public byte[] RowVersion { get; set; }
        public string Name { get; set; }
        public int SettingChoice_SettingCategory { get; set; }
    
        public virtual SettingCategory SettingCategory { get; set; }
        public virtual ICollection<SettingChoiceToExecutionResultSet> SettingChoiceToExecutionResultSets { get; set; }
        public virtual ICollection<SettingChoiceToSettingChoiceSet> SettingChoiceToSettingChoiceSets { get; set; }
    }
}
