Imports System
Imports System.Data

' Representative legacy code, deliberately preserved as a migration baseline.
' Callers must supply valid rows. Column names and business rules are coupled.
Public Module InvoiceCalculator
    Public Function Calculate(lines As DataTable, customerType As String) As Decimal()
        Dim subtotal As Decimal = 0D
        For Each row As DataRow In lines.Rows
            subtotal += CDec(row("UnitPrice")) * CInt(row("Quantity"))
        Next

        Dim discount As Decimal = 0D
        If customerType = "Trade" AndAlso subtotal >= 1000D Then
            discount = Math.Round(subtotal * 0.1D, 2, MidpointRounding.AwayFromZero)
        End If
        Dim net = subtotal - discount
        Dim vat = Math.Round(net * 0.2D, 2, MidpointRounding.AwayFromZero)
        Return New Decimal() {subtotal, discount, vat, net + vat}
    End Function
End Module
