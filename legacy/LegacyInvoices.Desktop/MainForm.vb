Imports System
Imports System.Data
Imports System.Drawing
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Windows.Forms

' Presentation only: pricing, persistence and stock rules remain in the original store.
Public Class MainForm
    Inherits Form

    Private ReadOnly store As Global.LegacyInvoices.OrderStore
    Private ReadOnly tabs As New TabControl With {.Dock = DockStyle.Fill}
    Private ReadOnly catalogue As DataGridView = CreateGrid()
    Private ReadOnly history As DataGridView = CreateGrid()
    Private ReadOnly lowStock As DataGridView = CreateGrid()
    Private ReadOnly customerGrid As DataGridView = CreateGrid()
    Private ReadOnly cartGrid As DataGridView = CreateGrid()
    Private ReadOnly search As New TextBox With {.Width = 300}
    Private ReadOnly customer As ComboBox = CreateCombo()
    Private ReadOnly product As ComboBox = CreateCombo()
    Private ReadOnly historyCustomer As ComboBox = CreateCombo()
    Private ReadOnly historyStatus As ComboBox = CreateCombo()
    Private ReadOnly quantity As New NumericUpDown With {.Minimum = 1, .Maximum = 10000, .Value = 1, .Width = 80}
    Private ReadOnly summary As New Label With {.AutoSize = True, .Font = New Font("Segoe UI", 13), .Padding = New Padding(12)}
    Private ReadOnly totals As New Label With {.AutoSize = True, .Padding = New Padding(8)}
    Private ReadOnly accountHint As New Label With {.AutoSize = True, .Padding = New Padding(6)}
    Private ReadOnly placeButton As New Button With {.Text = "Place order", .AutoSize = True, .Enabled = False}
    Private ReadOnly cart As New DataTable()
    Private refreshing As Boolean

    Public Sub New(orderStore As Global.LegacyInvoices.OrderStore, dataFile As String)
        store = orderStore
        Text = "Legacy Order Desk | VB.NET / .NET Framework 4.8"
        Size = New Size(1120, 760)
        MinimumSize = New Size(960, 620)
        StartPosition = FormStartPosition.CenterScreen
        Font = New Font("Segoe UI", 10)
        AutoScaleMode = AutoScaleMode.Dpi

        Dim heading As New Label With {.Text = "LEGACY ORDER DESK   /   Sales & inventory", .Dock = DockStyle.Top, .Height = 62, .Padding = New Padding(16), .BackColor = Color.FromArgb(34, 52, 72), .ForeColor = Color.White, .Font = New Font("Segoe UI", 17, FontStyle.Bold)}
        Dim footer As New StatusStrip()
        footer.Items.Add(New ToolStripStatusLabel("XML store: " & Path.GetFileName(dataFile)) With {.ToolTipText = Path.GetFullPath(dataFile)})
        Controls.Add(tabs)
        Controls.Add(heading)
        Controls.Add(footer)

        BuildOverview()
        BuildOrderEntry()
        BuildCatalogue()
        BuildCustomers()
        BuildHistory()
        AddHandler Shown, Sub() RunSafely(AddressOf RefreshAll)
    End Sub

    Private Shared Function CreateGrid() As DataGridView
        Return New DataGridView With {.Dock = DockStyle.Fill, .ReadOnly = True, .AllowUserToAddRows = False, .AllowUserToDeleteRows = False, .AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, .SelectionMode = DataGridViewSelectionMode.FullRowSelect, .MultiSelect = False, .RowHeadersVisible = False, .BackgroundColor = Color.White, .BorderStyle = BorderStyle.Fixed3D, .AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells}
    End Function

    Private Shared Function CreateCombo() As ComboBox
        Return New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 280}
    End Function

    Private Function Page(title As String) As TabPage
        Dim result As New TabPage(title) With {.Padding = New Padding(12), .BackColor = Color.WhiteSmoke}
        tabs.TabPages.Add(result)
        Return result
    End Function

    Private Shared Function Toolbar(ParamArray controls As Control()) As FlowLayoutPanel
        Dim bar As New FlowLayoutPanel With {.Dock = DockStyle.Top, .AutoSize = True, .Padding = New Padding(0, 6, 0, 6), .WrapContents = True}
        bar.Controls.AddRange(controls)
        Return bar
    End Function

    Private Function ActionButton(caption As String, action As Action) As Button
        Dim button As New Button With {.Text = caption, .AutoSize = True, .Padding = New Padding(6, 2, 6, 2)}
        AddHandler button.Click, Sub() RunSafely(action)
        Return button
    End Function

    Private Sub RunSafely(action As Action)
        Try
            action()
        Catch ex As Exception
            MessageBox.Show(Me, ex.Message, "Please check this action", MessageBoxButtons.OK, MessageBoxIcon.Warning)
        End Try
    End Sub

    Private Sub BuildOverview()
        Dim overview = Page("Overview")
        overview.Controls.Add(lowStock)
        overview.Controls.Add(Toolbar(New Label With {.Text = "Low stock (5 or fewer units)", .AutoSize = True}, ActionButton("Refresh dashboard", AddressOf RefreshAll)))
        summary.Dock = DockStyle.Top
        overview.Controls.Add(summary)
    End Sub

    Private Sub BuildCatalogue()
        Dim view = Page("Catalogue")
        view.Controls.Add(catalogue)
        view.Controls.Add(Toolbar(New Label With {.Text = "Search SKU, name or category", .AutoSize = True}, search))
        AddHandler search.TextChanged, Sub() RunSafely(Sub() BindGrid(catalogue, store.GetProducts(search.Text)))
    End Sub

    Private Sub BuildCustomers()
        Dim view = Page("Customers")
        view.Controls.Add(customerGrid)
        view.Controls.Add(Toolbar(New Label With {.Text = "Trade accounts: 10% discount from £1,000 before VAT.", .AutoSize = True}, ActionButton("View customer orders", Sub()
            If customerGrid.CurrentRow Is Nothing Then Return
            historyCustomer.SelectedValue = CStr(customerGrid.CurrentRow.Cells("Id").Value)
            tabs.SelectedIndex = 4
        End Sub)))
    End Sub

    Private Sub BuildOrderEntry()
        cart.Columns.Add("Sku", GetType(String))
        cart.Columns.Add("Name", GetType(String))
        cart.Columns.Add("UnitPrice", GetType(Decimal))
        cart.Columns.Add("Quantity", GetType(Integer))
        cart.PrimaryKey = New DataColumn() {cart.Columns("Sku")}
        ' Hidden tabs may defer automatic column creation until their handles exist.
        ' Define the cart columns before accessing their formatting settings.
        cartGrid.AutoGenerateColumns = False
        For Each field As String In New String() {"Sku", "Name", "UnitPrice", "Quantity"}
            cartGrid.Columns.Add(New DataGridViewTextBoxColumn With {.Name = field, .DataPropertyName = field, .HeaderText = field})
        Next
        cartGrid.DataSource = cart
        cartGrid.ReadOnly = False
        For Each column As DataGridViewColumn In cartGrid.Columns
            column.ReadOnly = column.Name <> "Quantity"
        Next
        cartGrid.Columns("UnitPrice").DefaultCellStyle.Format = "C2"
        cartGrid.Columns("UnitPrice").DefaultCellStyle.FormatProvider = CultureInfo.GetCultureInfo("en-GB")
        AddHandler cartGrid.CellValueChanged, Sub() InvalidateQuote()
        AddHandler cartGrid.DataError, Sub(sender, e)
            e.ThrowException = False
            e.Cancel = True
            InvalidateQuote()
            MessageBox.Show(Me, "Enter a whole-number quantity between 1 and 10,000.", "Invalid quantity")
        End Sub
        AddHandler cartGrid.CellValidating, Sub(sender, e)
            If cartGrid.Columns(e.ColumnIndex).Name <> "Quantity" Then Return
            Dim value As Integer
            If Not Integer.TryParse(CStr(e.FormattedValue), value) OrElse value < 1 OrElse value > 10000 Then
                e.Cancel = True
                cartGrid.Rows(e.RowIndex).ErrorText = "Quantity must be a whole number from 1 to 10,000. Press Esc to undo."
                InvalidateQuote()
            Else
                cartGrid.Rows(e.RowIndex).ErrorText = ""
            End If
        End Sub
        Dim view = Page("New order")
        view.Controls.Add(cartGrid)
        Dim bottom As New FlowLayoutPanel With {.Dock = DockStyle.Bottom, .Height = 115, .FlowDirection = FlowDirection.TopDown}
        bottom.Controls.Add(totals)
        bottom.Controls.Add(Toolbar(ActionButton("Remove selected line", AddressOf RemoveLine), ActionButton("Review totals", AddressOf ReviewQuote), placeButton))
        view.Controls.Add(bottom)
        view.Controls.Add(Toolbar(product, quantity, ActionButton("Add product", AddressOf AddLine)))
        view.Controls.Add(Toolbar(New Label With {.Text = "Customer", .AutoSize = True}, customer, accountHint))
        AddHandler customer.SelectedValueChanged, Sub()
            InvalidateQuote()
            Dim selected = TryCast(customer.SelectedItem, DataRowView)
            accountHint.Text = If(selected Is Nothing, "", CStr(selected("Type")) & " account | VAT 20%")
        End Sub
        AddHandler placeButton.Click, Sub() RunSafely(AddressOf PlaceOrder)
        InvalidateQuote()
    End Sub

    Private Sub BuildHistory()
        Dim view = Page("Order history")
        historyStatus.Width = 120
        historyStatus.Items.AddRange(New Object() {"All statuses", "Placed", "Cancelled"})
        historyStatus.SelectedIndex = 0
        view.Controls.Add(history)
        view.Controls.Add(Toolbar(historyCustomer, historyStatus, ActionButton("Order details", AddressOf ShowDetails), ActionButton("Cancel order", AddressOf CancelOrder), ActionButton("Export all CSV", AddressOf ExportOrders)))
        AddHandler historyCustomer.SelectedValueChanged, Sub()
            If Not refreshing Then RunSafely(AddressOf RefreshHistory)
        End Sub
        AddHandler historyStatus.SelectedIndexChanged, Sub()
            If Not refreshing Then RunSafely(AddressOf RefreshHistory)
        End Sub
        AddHandler history.CellDoubleClick, Sub(sender, e)
            If e.RowIndex >= 0 Then RunSafely(AddressOf ShowDetails)
        End Sub
    End Sub

    Private Sub RefreshAll()
        refreshing = True
        Try
            Dim selectedCustomer = TryCast(customer.SelectedValue, String)
            Dim selectedProduct = TryCast(product.SelectedValue, String)
            Dim filterCustomer = TryCast(historyCustomer.SelectedValue, String)
            customer.DisplayMember = "Name"
            customer.ValueMember = "Id"
            customer.DataSource = store.GetCustomers()
            If selectedCustomer IsNot Nothing Then customer.SelectedValue = selectedCustomer
            product.DisplayMember = "Name"
            product.ValueMember = "Sku"
            product.DataSource = store.GetProducts()
            If selectedProduct IsNot Nothing Then product.SelectedValue = selectedProduct
            Dim filters = store.GetCustomers()
            Dim all = filters.NewRow()
            all.ItemArray = New Object() {"", "All customers", ""}
            filters.Rows.InsertAt(all, 0)
            historyCustomer.DisplayMember = "Name"
            historyCustomer.ValueMember = "Id"
            historyCustomer.DataSource = filters
            If filterCustomer IsNot Nothing Then historyCustomer.SelectedValue = filterCustomer
            BindGrid(catalogue, store.GetProducts(search.Text))
            BindGrid(customerGrid, store.GetCustomers())
            Dim stockView As New DataView(store.GetProducts()) With {.RowFilter = "Stock <= 5", .Sort = "Stock ASC"}
            lowStock.DataSource = stockView
            FormatMoney(lowStock)
            Dim report = store.GetReport()
            summary.Text = String.Format(CultureInfo.GetCultureInfo("en-GB"), "Sales {0:C2}    |    Active orders {1}    |    Cancelled {2}" & Environment.NewLine & "Net {3:C2}    VAT {4:C2}    Units sold {5}    Low-stock products {6}", report(4), report(0), report(1), report(2), report(3), report(5), report(6))
            RefreshHistory()
            InvalidateQuote()
        Finally
            refreshing = False
        End Try
    End Sub

    Private Sub RefreshHistory()
        Dim customerId = TryCast(historyCustomer.SelectedValue, String)
        Dim data = store.GetOrders(If(String.IsNullOrEmpty(customerId), Nothing, customerId))
        Dim view As New DataView(data)
        If historyStatus.SelectedIndex > 0 Then view.RowFilter = "Status = '" & CStr(historyStatus.SelectedItem) & "'"
        history.DataSource = view
        For Each name As String In New String() {"CustomerId", "CustomerType", "Subtotal", "Discount", "Vat"}
            If history.Columns.Contains(name) Then history.Columns(name).Visible = False
        Next
        FormatMoney(history)
    End Sub

    Private Shared Sub BindGrid(grid As DataGridView, data As DataTable)
        grid.DataSource = data
        FormatMoney(grid)
    End Sub

    Private Shared Sub FormatMoney(grid As DataGridView)
        For Each name As String In New String() {"UnitPrice", "Total"}
            If grid.Columns.Contains(name) Then
                grid.Columns(name).DefaultCellStyle.Format = "C2"
                grid.Columns(name).DefaultCellStyle.FormatProvider = CultureInfo.GetCultureInfo("en-GB")
            End If
        Next
    End Sub

    Private Sub InvalidateQuote()
        placeButton.Enabled = False
        totals.Text = "Review totals before placing an order. Quotes do not reserve stock."
    End Sub

    Private Sub AddLine()
        Dim selected = TryCast(product.SelectedItem, DataRowView)
        If selected Is Nothing Then Return
        Dim existing = cart.Rows.Find(selected("Sku"))
        If existing Is Nothing Then
            cart.Rows.Add(selected("Sku"), selected("Name"), selected("UnitPrice"), CInt(quantity.Value))
        Else
            Dim combined = CInt(existing("Quantity")) + CInt(quantity.Value)
            If combined > 10000 Then Throw New ArgumentException("A product quantity cannot exceed 10,000.")
            existing("Quantity") = combined
        End If
        InvalidateQuote()
    End Sub

    Private Sub RemoveLine()
        If cartGrid.CurrentRow Is Nothing Then Return
        cart.Rows.Remove(DirectCast(cartGrid.CurrentRow.DataBoundItem, DataRowView).Row)
        InvalidateQuote()
    End Sub

    Private Sub CommitEdits()
        If Not cartGrid.EndEdit() Then Throw New ArgumentException("Correct the highlighted quantity first.")
        BindingContext(cart).EndCurrentEdit()
    End Sub

    Private Function Skus() As String()
        Return cart.Rows.Cast(Of DataRow)().Select(Function(row) CStr(row("Sku"))).ToArray()
    End Function

    Private Function Quantities() As Integer()
        Return cart.Rows.Cast(Of DataRow)().Select(Function(row) CInt(row("Quantity"))).ToArray()
    End Function

    Private Sub ReviewQuote()
        InvalidateQuote()
        CommitEdits()
        Dim values = store.Quote(CStr(customer.SelectedValue), Skus(), Quantities())
        totals.Text = String.Format(CultureInfo.GetCultureInfo("en-GB"), "Subtotal {0:C2}     Discount {1:C2}     VAT {2:C2}     TOTAL {3:C2}", values(0), values(1), values(2), values(3))
        placeButton.Enabled = True
    End Sub

    Private Sub PlaceOrder()
        If Not placeButton.Enabled Then Return
        CommitEdits()
        InvalidateQuote()
        Dim id = store.Place(CStr(customer.SelectedValue), Skus(), Quantities())
        cart.Clear()
        MessageBox.Show(Me, "Your order has been saved and stock updated." & Environment.NewLine & Environment.NewLine & id, "Order created successfully", MessageBoxButtons.OK, MessageBoxIcon.Information)
        RefreshAll()
    End Sub

    Private Function SelectedOrderId() As String
        If history.CurrentRow Is Nothing Then Throw New ArgumentException("Select an order from the history first.")
        Return CStr(history.CurrentRow.Cells("Id").Value)
    End Function

    Private Sub ShowDetails()
        Dim id = SelectedOrderId()
        Using details As New OrderDetailsForm(store.GetOrder(id), store.GetOrderLines(id))
            details.ShowDialog(Me)
        End Using
    End Sub

    Private Sub CancelOrder()
        Dim id = SelectedOrderId()
        If MessageBox.Show(Me, "Cancel this order and restore its stock?" & Environment.NewLine & id, "Confirm cancellation", MessageBoxButtons.YesNo, MessageBoxIcon.Question) <> DialogResult.Yes Then Return
        store.Cancel(id)
        RefreshAll()
        MessageBox.Show(Me, "Order cancelled. Stock has been restored.", "Cancellation complete", MessageBoxButtons.OK, MessageBoxIcon.Information)
    End Sub

    Private Sub ExportOrders()
        Using dialog As New SaveFileDialog With {.Filter = "CSV files (*.csv)|*.csv", .FileName = "legacy-orders.csv", .Title = "Export all order history"}
            If dialog.ShowDialog(Me) <> DialogResult.OK Then Return
            File.WriteAllText(dialog.FileName, store.ExportCsv())
            MessageBox.Show(Me, "Order history exported successfully.", "Export complete", MessageBoxButtons.OK, MessageBoxIcon.Information)
        End Using
    End Sub
End Class
