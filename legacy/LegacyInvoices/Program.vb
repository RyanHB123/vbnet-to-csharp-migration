Imports System
Imports System.Data
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports Microsoft.VisualBasic

Module Program
    Function Main(args As String()) As Integer
        Try
            If args.Length > 0 AndAlso args(0) = "--interactive" Then
                Console.WriteLine("LEGACY ORDER DESK | VB.NET / .NET Framework 4.8")
                Help()
                Do
                    Console.Write(vbCrLf & "legacy> ")
                    Dim input = Console.ReadLine()
                    If input Is Nothing OrElse input.Trim() = "exit" Then Exit Do
                    Try
                        Execute(input.Split(New Char() {" "c}, StringSplitOptions.RemoveEmptyEntries))
                    Catch ex As Exception
                        Console.WriteLine("Error: " & ex.Message)
                    End Try
                Loop
            Else
                Execute(args)
            End If
            Return 0
        Catch ex As Exception
            Console.Error.WriteLine("Error: " & ex.Message)
            Return 1
        End Try
    End Function

    Private Sub Help()
        Console.WriteLine("products [search] | customers | orders [customer-id] | report")
        Console.WriteLine("quote CUST-001 MON-001:8 DOCK-002:2")
        Console.WriteLine("place CUST-001 MON-001:8 DOCK-002:2")
        Console.WriteLine("cancel order-id | export | invoice | help | exit")
        Console.WriteLine("Set LEGACY_DATA_FILE to choose a separate XML data file.")
    End Sub

    Private Sub Execute(args As String())
        Dim command = If(args.Length = 0, "invoice", args(0).ToLowerInvariant())
        If command = "invoice" Then
            SampleInvoice()
            Return
        End If
        Dim path = Environment.GetEnvironmentVariable("LEGACY_DATA_FILE")
        If String.IsNullOrWhiteSpace(path) Then path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "orders.xml")
        Dim store As New OrderStore(path)
        Select Case command
            Case "products"
                PrintTable(store.GetProducts(If(args.Length > 1, String.Join(" ", args.Skip(1)), "")))
            Case "customers"
                PrintTable(store.GetCustomers())
            Case "orders"
                PrintTable(store.GetOrders(If(args.Length > 1, args(1), Nothing)))
            Case "quote", "place"
                If args.Length < 3 Then Throw New ArgumentException("Supply customer-id and SKU:quantity pairs.")
                Dim skus As New System.Collections.Generic.List(Of String)()
                Dim quantities As New System.Collections.Generic.List(Of Integer)()
                For Each item In args.Skip(2)
                    Dim pair = item.Split(":"c)
                    If pair.Length <> 2 Then Throw New ArgumentException("Use SKU:quantity.")
                    skus.Add(pair(0))
                    quantities.Add(Integer.Parse(pair(1), CultureInfo.InvariantCulture))
                Next
                If command = "quote" Then
                    PrintTotals(store.Quote(args(1), skus.ToArray(), quantities.ToArray()))
                Else
                    Console.WriteLine("Placed: " & store.Place(args(1), skus.ToArray(), quantities.ToArray()))
                End If
            Case "cancel"
                If args.Length <> 2 Then Throw New ArgumentException("Supply an order-id.")
                store.Cancel(args(1))
                Console.WriteLine("Cancelled; stock restored.")
            Case "report"
                Dim report = store.GetReport()
                Dim labels = {"ActiveOrders", "CancelledOrders", "NetSales", "VAT", "GrossSales", "UnitsSold", "LowStockProducts"}
                For i = 0 To labels.Length - 1
                    Console.WriteLine(labels(i) & ": " & report(i).ToString("0.00", CultureInfo.InvariantCulture))
                Next
            Case "export"
                Console.Write(store.ExportCsv())
            Case "help"
                Help()
            Case Else
                Throw New ArgumentException("Unknown command. Use help.")
        End Select
    End Sub

    Private Sub PrintTable(table As DataTable)
        Console.WriteLine(String.Join(" | ", table.Columns.Cast(Of DataColumn)().Select(Function(c) c.ColumnName)))
        For Each row As DataRow In table.Rows
            Console.WriteLine(String.Join(" | ", row.ItemArray.Select(Function(value) Convert.ToString(value, CultureInfo.InvariantCulture))))
        Next
        If table.Rows.Count = 0 Then Console.WriteLine("No records yet.")
    End Sub

    Private Sub SampleInvoice()
        Dim lines As New DataTable()
        lines.Columns.Add("UnitPrice", GetType(Decimal))
        lines.Columns.Add("Quantity", GetType(Integer))
        lines.Rows.Add(125D, 8)
        lines.Rows.Add(49.5D, 2)
        Dim totals = InvoiceCalculator.Calculate(lines, "Trade")
        PrintTotals(totals)
    End Sub

    Private Sub PrintTotals(totals As Decimal())
        Dim names = {"Subtotal", "Discount", "VAT", "Total"}
        For index = 0 To totals.Length - 1
            Console.WriteLine(names(index) & ": " & totals(index).ToString("F2", CultureInfo.InvariantCulture))
        Next
    End Sub
End Module
