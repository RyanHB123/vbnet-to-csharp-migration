Imports System.Data
Imports Microsoft.VisualBasic
Imports System.Drawing
Imports System.Globalization
Imports System.Windows.Forms

Public Class OrderDetailsForm
    Inherits Form

    Public Sub New(order As DataRow, lines As DataTable)
        Text = "Saved order details"
        Size = New Size(850, 500)
        MinimumSize = New Size(650, 400)
        StartPosition = FormStartPosition.CenterParent
        Font = New Font("Segoe UI", 10)
        Dim heading As New Label With {.Dock = DockStyle.Top, .Height = 90, .Padding = New Padding(12), .Text = CStr(order("Id")) & vbCrLf & CStr(order("CustomerName")) & " | " & CStr(order("CustomerType")) & " | " & CStr(order("Status")) & vbCrLf & "Created: " & CStr(order("CreatedAt"))}
        Dim grid As New DataGridView With {.Dock = DockStyle.Fill, .ReadOnly = True, .AllowUserToAddRows = False, .AllowUserToDeleteRows = False, .RowHeadersVisible = False, .AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, .AutoGenerateColumns = False, .BackgroundColor = Color.White}
        For Each field As String In New String() {"OrderId", "Sku", "Name", "UnitPrice", "Quantity"}
            grid.Columns.Add(New DataGridViewTextBoxColumn With {.Name = field, .DataPropertyName = field, .HeaderText = field})
        Next
        grid.DataSource = lines
        grid.Columns("OrderId").Visible = False
        grid.Columns("UnitPrice").DefaultCellStyle.Format = "C2"
        grid.Columns("UnitPrice").DefaultCellStyle.FormatProvider = CultureInfo.GetCultureInfo("en-GB")
        Dim totals As New Label With {.Dock = DockStyle.Bottom, .Height = 55, .Padding = New Padding(12), .Text = String.Format(CultureInfo.GetCultureInfo("en-GB"), "Subtotal {0:C2}    Discount {1:C2}    VAT {2:C2}    TOTAL {3:C2}", order("Subtotal"), order("Discount"), order("Vat"), order("Total"))}
        Dim close As New Button With {.Text = "Close", .Dock = DockStyle.Bottom, .Height = 36, .DialogResult = DialogResult.OK}
        CancelButton = close
        Controls.Add(grid)
        Controls.Add(heading)
        Controls.Add(totals)
        Controls.Add(close)
    End Sub
End Class
