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
    
    public partial class RowList
    {
        public RowList()
        {
            this.ReportSnippets = new HashSet<ReportSnippet>();
            this.RowListToRows = new HashSet<RowListToRow>();
        }
    
        public int Id { get; set; }
        public byte[] RowVersion { get; set; }
        public string Name { get; set; }
    
        public virtual ICollection<ReportSnippet> ReportSnippets { get; set; }
        public virtual ICollection<RowListToRow> RowListToRows { get; set; }
    }
}
