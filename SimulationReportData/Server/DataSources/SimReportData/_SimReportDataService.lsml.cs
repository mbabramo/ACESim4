using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.LightSwitch;
using Microsoft.LightSwitch.Security.Server;
using System.Linq.Expressions;
namespace LightSwitchApplication
{
    public partial class SimReportDataService
    {
        partial void DataPointsFiltered_PreprocessQuery(int? selectedExecutionResultSetId, int? selectedRowId, int? selectedColumnId, int? selectedRowListId, int? selectedColumnListId, int? selectedReportSnippetId, int? selectedChoice1Id, int? selectedChoice2Id, int? selectedChoice3Id, int? selectedChoice4Id, int? selectedChoice5Id, int? selectedChoice6Id, int? selectedChoice7Id, int? selectedChoice8Id, ref IQueryable<DataPoint> query)
        {
            // narrow down by result set
            if (selectedExecutionResultSetId == null)
            {
                List<int?> selectedChoiceIds = new List<int?> { selectedChoice1Id, selectedChoice2Id, selectedChoice3Id, selectedChoice4Id, selectedChoice5Id, selectedChoice6Id, selectedChoice7Id, selectedChoice8Id };
                foreach (int? selectedChoice in selectedChoiceIds)
                    if (selectedChoice != null)
                        query = query.Where(x => x.ExecutionResultSet.SettingChoiceToExecutionResultSets.Any(y => y.SettingChoice.Id == selectedChoice));
            }
            else
                query = query.Where(x => x.ExecutionResultSet.Id == selectedExecutionResultSetId);

            // resolve the row snippet id
            if (selectedReportSnippetId != null)
            {
                ReportSnippet rs = this.DataWorkspace.SimReportData.ReportSnippets_Single((int)selectedReportSnippetId);
                selectedRowListId = rs.RowList.Id;
                selectedColumnListId = rs.ColumnList.Id;
            }

            // narrow down by rows
            bool noRowsSelected = false;
            if (selectedRowListId != null)
                query = query.Where(x => x.Row.RowListToRows.Any(z => z.RowList.Id == selectedRowListId));
            else if (selectedRowId != null)
                query = query.Where(x => x.Row.Id == selectedRowId);
            else
                noRowsSelected = true;

            // narrow down by Columns
            bool noColumnsSelected = false;
            if (selectedColumnListId != null)
                query = query.Where(x => x.Column.ColumnListToColumns.Any(z => z.ColumnList.Id == selectedColumnListId));
            else if (selectedColumnId != null)
                query = query.Where(x => x.Column.Id == selectedColumnId);
            else
                noColumnsSelected = true;

            if (noRowsSelected && noColumnsSelected)
                query = query.Where(x => false); // return nothing



            // Order the list based on the row list order or column list order specified, or if none is specified, then by id.
            // Note that the entity framework requires that FirstOrDefault be used rather than Single or SingleOrDefault or First, since it is not the last item in the query.
            Expression<Func<DataPoint, int?>> rowListOrder = x => x.Row.RowListToRows.FirstOrDefault(y => y.RowList.Id == selectedRowListId).OrderInList;
            Expression<Func<DataPoint, int?>> columnListOrder = z => z.Column.ColumnListToColumns.FirstOrDefault(w => w.ColumnList.Id == selectedColumnListId).OrderInList;
            if (selectedRowListId != null && selectedColumnListId != null)
                query = query.OrderBy(rowListOrder).ThenBy(columnListOrder);
            else if (selectedRowListId != null)
                query = query.OrderBy(rowListOrder).ThenBy(x => x.Column.Id);
            else if (selectedColumnListId != null)
                query = query.OrderBy(x => x.Row.Id).ThenBy(columnListOrder);
            else
                query = query.OrderBy(x => x.Row.Id).ThenBy(y => y.Column.Id);
        }

        partial void ExecutionResultSetsForSpecifiedSettingChoices_PreprocessQuery(int? selectedChoice1Id, int? selectedChoice2Id, int? selectedChoice3Id, int? selectedChoice4Id, int? selectedChoice5Id, int? selectedChoice6Id, int? selectedChoice7Id, int? selectedChoice8Id, ref IQueryable<ExecutionResultSet> query)
        {
            List<int?> selectedChoiceIds = new List<int?> { selectedChoice1Id, selectedChoice2Id, selectedChoice3Id, selectedChoice4Id, selectedChoice5Id, selectedChoice6Id, selectedChoice7Id, selectedChoice8Id };
            foreach (int? selectedChoice in selectedChoiceIds)
                if (selectedChoice != null)
                    query = query.Where(x => x.SettingChoiceToExecutionResultSets.Any(y => y.SettingChoice.Id == selectedChoice));
        }

    }
}
