Imports System
Imports System.IO
Imports System.Windows.Forms

Public Module Startup
    <STAThread>
    Public Sub Main()
        Application.EnableVisualStyles()
        Application.SetCompatibleTextRenderingDefault(False)
        Try
            Dim dataFile = Environment.GetEnvironmentVariable("LEGACY_DATA_FILE")
            If String.IsNullOrWhiteSpace(dataFile) Then dataFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "orders.xml")
            Application.Run(New MainForm(New Global.LegacyInvoices.OrderStore(dataFile), dataFile))
        Catch ex As Exception
            MessageBox.Show(ex.Message, "Unable to open Legacy Order Desk", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub
End Module
