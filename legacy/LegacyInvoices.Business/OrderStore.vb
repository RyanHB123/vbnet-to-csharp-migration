Imports System
Imports System.Collections.Generic
Imports System.Data
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Text
Imports Microsoft.VisualBasic

' Deliberately uses a DataSet and XML persistence to represent an older application.
' One application process per file; mutations are saved only after validation succeeds.
Public Class OrderStore
    Private ReadOnly filePath As String
    Private ReadOnly gate As New Object()

    Public Sub New(path As String)
        filePath = System.IO.Path.GetFullPath(path)
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(filePath))
    End Sub

    Private Function Load() As DataSet
        If File.Exists(filePath) Then
            Dim existing As New DataSet()
            existing.ReadXml(filePath, XmlReadMode.ReadSchema)
            If existing.DataSetName <> "OrderStoreV1" Then Throw New InvalidDataException("Unsupported order data version.")
            Return existing
        End If
        Dim data As New DataSet("OrderStoreV1")
        Dim products = data.Tables.Add("Products")
        products.Columns.Add("Sku", GetType(String))
        products.Columns.Add("Name", GetType(String))
        products.Columns.Add("Category", GetType(String))
        products.Columns.Add("UnitPrice", GetType(Decimal))
        products.Columns.Add("Stock", GetType(Integer))
        products.PrimaryKey = New DataColumn() {products.Columns("Sku")}
        products.Rows.Add("MON-001", "27-inch Studio Monitor", "Displays", 125D, 24)
        products.Rows.Add("DOCK-002", "USB-C Desktop Dock", "Accessories", 49.5D, 40)
        products.Rows.Add("KEY-003", "Mechanical Keyboard", "Accessories", 79.95D, 18)
        products.Rows.Add("CAM-004", "HD Conference Camera", "Video", 89D, 8)
        products.Rows.Add("ARM-005", "Adjustable Monitor Arm", "Workspace", 64.5D, 5)
        products.Rows.Add("HUB-006", "7-Port USB Hub", "Accessories", 29.99D, 3)
        Dim customers = data.Tables.Add("Customers")
        customers.Columns.Add("Id", GetType(String))
        customers.Columns.Add("Name", GetType(String))
        customers.Columns.Add("Type", GetType(String))
        customers.PrimaryKey = New DataColumn() {customers.Columns("Id")}
        customers.Rows.Add("CUST-001", "Northwind Studio", "Trade")
        customers.Rows.Add("CUST-002", "Alex Morgan", "Retail")
        customers.Rows.Add("CUST-003", "Harbour Design Co.", "Trade")
        Dim orders = data.Tables.Add("Orders")
        For Each name In New String() {"Id", "CustomerId", "CustomerName", "CustomerType", "CreatedAt", "Status"}
            orders.Columns.Add(name, GetType(String))
        Next
        For Each name In New String() {"Subtotal", "Discount", "Vat", "Total"}
            orders.Columns.Add(name, GetType(Decimal))
        Next
        orders.PrimaryKey = New DataColumn() {orders.Columns("Id")}
        Dim lines = data.Tables.Add("Lines")
        For Each name In New String() {"OrderId", "Sku", "Name"}
            lines.Columns.Add(name, GetType(String))
        Next
        lines.Columns.Add("UnitPrice", GetType(Decimal))
        lines.Columns.Add("Quantity", GetType(Integer))
        Return data
    End Function

    Private Sub Save(data As DataSet)
        Dim temporary = filePath & "." & Guid.NewGuid().ToString("N") & ".tmp"
        Try
            data.WriteXml(temporary, XmlWriteMode.WriteSchema)
            If File.Exists(filePath) Then
                File.Replace(temporary, filePath, Nothing)
            Else
                File.Move(temporary, filePath)
            End If
        Finally
            If File.Exists(temporary) Then File.Delete(temporary)
        End Try
    End Sub

    Public Function GetProducts(Optional search As String = "") As DataTable
        SyncLock gate
            Dim products = Load().Tables("Products")
            Dim result = products.Clone()
            For Each row As DataRow In products.Rows
                If (CStr(row("Sku")) & " " & CStr(row("Name")) & " " & CStr(row("Category"))).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 Then result.ImportRow(row)
            Next
            Return result
        End SyncLock
    End Function

    Public Function GetCustomers() As DataTable
        SyncLock gate
            Return Load().Tables("Customers")
        End SyncLock
    End Function

    Public Function GetOrders(Optional customerId As String = Nothing) As DataTable
        SyncLock gate
            Dim orders = Load().Tables("Orders")
            Dim result = orders.Clone()
            For Each row In orders.Rows.Cast(Of DataRow)().OrderByDescending(Function(r) CStr(r("CreatedAt")))
                If customerId Is Nothing OrElse CStr(row("CustomerId")) = customerId Then result.ImportRow(row)
            Next
            Return result
        End SyncLock
    End Function

    Public Function GetOrder(id As String) As DataRow
        SyncLock gate
            Dim order = Load().Tables("Orders").Rows.Find(id)
            If order Is Nothing Then Throw New ArgumentException("Order was not found.")
            Return order
        End SyncLock
    End Function

    Public Function GetOrderLines(id As String) As DataTable
        SyncLock gate
            Dim data = Load()
            If data.Tables("Orders").Rows.Find(id) Is Nothing Then Throw New ArgumentException("Order was not found.")
            Dim result = data.Tables("Lines").Clone()
            For Each line As DataRow In data.Tables("Lines").Rows
                If CStr(line("OrderId")) = id Then result.ImportRow(line)
            Next
            Return result
        End SyncLock
    End Function

    Private Function Prepare(data As DataSet, customerId As String, skus As String(), quantities As Integer()) As DataTable
        If skus Is Nothing OrElse quantities Is Nothing OrElse skus.Length < 1 OrElse skus.Length > 100 OrElse skus.Length <> quantities.Length Then Throw New ArgumentException("Choose between 1 and 100 products with quantities.")
        If data.Tables("Customers").Rows.Find(customerId) Is Nothing Then Throw New ArgumentException("Customer was not found.")
        Dim seen As New HashSet(Of String)(StringComparer.Ordinal)
        Dim lines = data.Tables("Lines").Clone()
        For index = 0 To skus.Length - 1
            If String.IsNullOrWhiteSpace(skus(index)) OrElse Not seen.Add(skus(index)) Then Throw New ArgumentException("Each product must have a unique SKU.")
            Dim product = data.Tables("Products").Rows.Find(skus(index))
            If product Is Nothing Then Throw New ArgumentException("Product was not found.")
            If quantities(index) < 1 OrElse quantities(index) > 10000 Then Throw New ArgumentException("Quantity must be between 1 and 10,000.")
            If quantities(index) > CInt(product("Stock")) Then Throw New InvalidOperationException("Insufficient stock for " & skus(index))
            lines.Rows.Add("", skus(index), product("Name"), product("UnitPrice"), quantities(index))
        Next
        Return lines
    End Function

    Public Function Quote(customerId As String, skus As String(), quantities As Integer()) As Decimal()
        SyncLock gate
            Dim data = Load()
            Dim lines = Prepare(data, customerId, skus, quantities)
            Return InvoiceCalculator.Calculate(lines, CStr(data.Tables("Customers").Rows.Find(customerId)("Type")))
        End SyncLock
    End Function

    Public Function Place(customerId As String, skus As String(), quantities As Integer()) As String
        SyncLock gate
            Dim data = Load()
            Dim lines = Prepare(data, customerId, skus, quantities)
            Dim customer = data.Tables("Customers").Rows.Find(customerId)
            Dim totals = InvoiceCalculator.Calculate(lines, CStr(customer("Type")))
            Dim id = "ORD-" & Guid.NewGuid().ToString("N")
            data.Tables("Orders").Rows.Add(id, customerId, customer("Name"), customer("Type"), DateTimeOffset.UtcNow.ToString("O"), "Placed", totals(0), totals(1), totals(2), totals(3))
            For Each line As DataRow In lines.Rows
                line("OrderId") = id
                data.Tables("Lines").ImportRow(line)
                Dim product = data.Tables("Products").Rows.Find(line("Sku"))
                product("Stock") = CInt(product("Stock")) - CInt(line("Quantity"))
            Next
            Save(data)
            Return id
        End SyncLock
    End Function

    Public Sub Cancel(id As String)
        SyncLock gate
            Dim data = Load()
            Dim order = data.Tables("Orders").Rows.Find(id)
            If order Is Nothing Then Throw New ArgumentException("Order was not found.")
            If CStr(order("Status")) = "Cancelled" Then Throw New InvalidOperationException("Order is already cancelled.")
            For Each line As DataRow In data.Tables("Lines").Rows
                If CStr(line("OrderId")) = id Then
                    Dim product = data.Tables("Products").Rows.Find(line("Sku"))
                    product("Stock") = CInt(product("Stock")) + CInt(line("Quantity"))
                End If
            Next
            order("Status") = "Cancelled"
            Save(data)
        End SyncLock
    End Sub

    Public Function GetReport() As Decimal()
        SyncLock gate
            Dim data = Load()
            Dim report(6) As Decimal
            Dim active As New HashSet(Of String)()
            For Each order As DataRow In data.Tables("Orders").Rows
                If CStr(order("Status")) = "Placed" Then
                    report(0) += 1
                    report(2) += CDec(order("Subtotal")) - CDec(order("Discount"))
                    report(3) += CDec(order("Vat"))
                    report(4) += CDec(order("Total"))
                    active.Add(CStr(order("Id")))
                Else
                    report(1) += 1
                End If
            Next
            For Each line As DataRow In data.Tables("Lines").Rows
                If active.Contains(CStr(line("OrderId"))) Then report(5) += CInt(line("Quantity"))
            Next
            For Each product As DataRow In data.Tables("Products").Rows
                If CInt(product("Stock")) <= 5 Then report(6) += 1
            Next
            Return report
        End SyncLock
    End Function

    Public Function ExportCsv() As String
        Dim csv As New StringBuilder("OrderId,Customer,Status,CreatedAt,Total" & vbCrLf)
        For Each row As DataRow In GetOrders().Rows
            csv.Append(CsvCell(CStr(row("Id")))).Append(",").Append(CsvCell(CStr(row("CustomerName")))).Append(",")
            csv.Append(row("Status")).Append(",").Append(row("CreatedAt")).Append(",")
            csv.Append(CDec(row("Total")).ToString("F2", CultureInfo.InvariantCulture)).Append(vbCrLf)
        Next
        Return csv.ToString()
    End Function

    Private Shared Function CsvCell(value As String) As String
        Return """" & value.Replace("""", """""") & """"
    End Function
End Class
