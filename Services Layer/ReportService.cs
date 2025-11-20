using System.Text;
using ContractMonthlyClaimSystem.Models;
using ContractMonthlyClaimSystem.Models.ViewModels;
using OfficeOpenXml;



namespace ContractMonthlyClaimSystem.Services
{
    public interface IReportService
    {
        Task<byte[]> GenerateInvoiceReportPdf(List<Claim> claims, DateTime startDate, DateTime endDate);
        Task<byte[]> GenerateInvoiceReportExcel(List<Claim> claims, DateTime startDate, DateTime endDate);
        Task<byte[]> GeneratePaymentBatchReport(PaymentBatch paymentBatch);
        Task<byte[]> GenerateCsvReport(List<Claim> claims);
    }

    public class EnhancedReportService : IReportService
    {
        static EnhancedReportService()
        {
            // Set the license for EPPlus 8+ (correct property)
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        }

        public Task<byte[]> GenerateInvoiceReportPdf(List<Claim> claims, System.DateTime startDate, System.DateTime endDate)
        {
            var htmlContent = GenerateInvoiceHtml(claims, startDate, endDate);
            return Task.FromResult(Encoding.UTF8.GetBytes(htmlContent));
        }

        public Task<byte[]> GenerateInvoiceReportExcel(List<Claim> claims, System.DateTime startDate, System.DateTime endDate)
        {
            using var package = new ExcelPackage();
            var worksheet = package.Workbook.Worksheets.Add("Invoices");

            // Add title and headers
            worksheet.Cells[1, 1].Value = "INVOICE REPORT";
            worksheet.Cells[1, 1].Style.Font.Bold = true;
            worksheet.Cells[1, 1].Style.Font.Size = 16;
            worksheet.Cells[1, 1, 1, 6].Merge = true;

            worksheet.Cells[2, 1].Value = $"Period: {startDate:dd MMMM yyyy} to {endDate:dd MMMM yyyy}";
            worksheet.Cells[2, 1, 2, 6].Merge = true;

            worksheet.Cells[3, 1].Value = $"Generated on: {DateTime.Now:dd MMMM yyyy 'at' HH:mm}";
            worksheet.Cells[3, 1, 3, 6].Merge = true;

            // Add headers
            worksheet.Cells[5, 1].Value = "Claim ID";
            worksheet.Cells[5, 2].Value = "Lecturer Name";
            worksheet.Cells[5, 3].Value = "Department";
            worksheet.Cells[5, 4].Value = "Hours Worked";
            worksheet.Cells[5, 5].Value = "Amount";
            worksheet.Cells[5, 6].Value = "Status";

            // Style headers
            using (var range = worksheet.Cells[5, 1, 5, 6])
            {
                range.Style.Font.Bold = true;
                range.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                range.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightBlue);
                range.Style.Border.BorderAround(OfficeOpenXml.Style.ExcelBorderStyle.Thin);
            }

            // Add data
            int row = 6;
            foreach (var claim in claims)
            {
                worksheet.Cells[row, 1].Value = claim.ClaimID;
                worksheet.Cells[row, 2].Value = claim.Lecturer?.Name ?? "N/A";
                worksheet.Cells[row, 3].Value = claim.Lecturer?.Department ?? "N/A";
                worksheet.Cells[row, 4].Value = claim.HoursWorked;
                worksheet.Cells[row, 5].Value = claim.Amount;
                worksheet.Cells[row, 5].Style.Numberformat.Format = "#,##0.00";
                worksheet.Cells[row, 6].Value = claim.Status;
                row++;
            }

            // Add total row
            worksheet.Cells[row, 1].Value = "TOTAL";
            worksheet.Cells[row, 1, row, 4].Merge = true;
            worksheet.Cells[row, 1].Style.Font.Bold = true;
            worksheet.Cells[row, 1].Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Right;

            worksheet.Cells[row, 5].Value = claims.Sum(c => c.Amount);
            worksheet.Cells[row, 5].Style.Numberformat.Format = "#,##0.00"; // Use plain currency without £ to avoid locale excel issues
            worksheet.Cells[row, 5].Style.Font.Bold = true;

            // Auto-fit columns
            worksheet.Cells[worksheet.Dimension.Address].AutoFitColumns();

            return Task.FromResult(package.GetAsByteArray());
        }

        public Task<byte[]> GeneratePaymentBatchReport(PaymentBatch paymentBatch)
        {
            using var package = new ExcelPackage();
            var worksheet = package.Workbook.Worksheets.Add("Payment Batch");

            // Add batch information
            worksheet.Cells[1, 1].Value = "PAYMENT BATCH REPORT";
            worksheet.Cells[1, 1].Style.Font.Bold = true;
            worksheet.Cells[1, 1].Style.Font.Size = 16;
            worksheet.Cells[1, 1, 1, 4].Merge = true;

            worksheet.Cells[3, 1].Value = "Batch Number:";
            worksheet.Cells[3, 2].Value = paymentBatch?.BatchNumber ?? "N/A";
            worksheet.Cells[3, 1].Style.Font.Bold = true;

            worksheet.Cells[4, 1].Value = "Generated Date:";
            worksheet.Cells[4, 2].Value = paymentBatch?.GeneratedDate.ToString("dd MMMM yyyy HH:mm") ?? "N/A";
            worksheet.Cells[4, 1].Style.Font.Bold = true;

            worksheet.Cells[5, 1].Value = "Generated By:";
            worksheet.Cells[5, 2].Value = paymentBatch?.GeneratedBy ?? "N/A";
            worksheet.Cells[5, 1].Style.Font.Bold = true;

            worksheet.Cells[6, 1].Value = "Total Claims:";
            worksheet.Cells[6, 2].Value = paymentBatch?.TotalClaims ?? 0;
            worksheet.Cells[6, 1].Style.Font.Bold = true;

            worksheet.Cells[7, 1].Value = "Total Amount:";
            worksheet.Cells[7, 2].Value = paymentBatch?.TotalAmount ?? 0;
            worksheet.Cells[7, 2].Style.Numberformat.Format = "#,##0.00";
            worksheet.Cells[7, 1].Style.Font.Bold = true;

            // Auto-fit columns
            worksheet.Cells[worksheet.Dimension.Address].AutoFitColumns();

            return Task.FromResult(package.GetAsByteArray());
        }

        public Task<byte[]> GenerateCsvReport(List<Claim> claims)
        {
            var csv = new StringBuilder();

            // Headers
            csv.AppendLine("ClaimID,LecturerName,Department,HoursWorked,Amount,Status,SubmissionDate");

            // Data rows
            foreach (var claim in claims)
            {
                csv.AppendLine(
                    $"{claim.ClaimID}," +
                    $"\"{EscapeCsvField(claim.Lecturer?.Name ?? "N/A")}\"," +
                    $"\"{EscapeCsvField(claim.Lecturer?.Department ?? "N/A")}\"," +
                    $"{claim.HoursWorked}," +
                    $"{claim.Amount}," +
                    $"\"{claim.Status}\"," +
                    $"\"{claim.SubmissionDate:yyyy-MM-dd}\""
                );
            }

            // Summary row
            csv.AppendLine($",,,Total,{claims.Sum(c => c.Amount)},,");

            return Task.FromResult(Encoding.UTF8.GetBytes(csv.ToString()));
        }

        private string GenerateInvoiceHtml(List<Claim> claims, System.DateTime startDate, System.DateTime endDate)
        {
            var sb = new StringBuilder();

            sb.AppendLine($@"
                <!DOCTYPE html>
                <html>
                <head>
                    <meta charset='UTF-8'>
                    <title>Invoice Report</title>
                    <style>
                        body {{ 
                            font-family: 'Arial', sans-serif; 
                            margin: 20px; 
                            color: #333;
                        }}
                        .header {{ 
                            text-align: center; 
                            margin-bottom: 30px; 
                            border-bottom: 2px solid #4E73DF; 
                            padding-bottom: 20px; 
                        }}
                        .summary {{ 
                            margin-bottom: 20px; 
                            background-color: #f8f9fa; 
                            padding: 15px; 
                            border-radius: 5px;
                            border-left: 4px solid #4E73DF;
                        }}
                        table {{ 
                            width: 100%; 
                            border-collapse: collapse; 
                            margin-bottom: 20px;
                            box-shadow: 0 0 10px rgba(0,0,0,0.1);
                        }}
                        th, td {{ 
                            border: 1px solid #ddd; 
                            padding: 12px; 
                            text-align: left; 
                        }}
                        th {{ 
                            background-color: #4E73DF; 
                            color: white;
                            font-weight: bold;
                        }}
                        tr:nth-child(even) {{ 
                            background-color: #f8f9fa; 
                        }}
                        .total-row {{ 
                            font-weight: bold; 
                            background-color: #e8f4f8; 
                        }}
                        .footer {{
                            margin-top: 30px;
                            text-align: center;
                            color: #6c757d;
                            font-size: 0.9em;
                        }}
                    </style>
                </head>
                <body>
                    <div class='header'>
                        <h1 style='color: #4E73DF; margin-bottom: 5px;'>INVOICE REPORT</h1>
                        <h3 style='color: #6c757d; margin-top: 5px;'>Contract Monthly Claim System</h3>
                        <p><strong>Period:</strong> {startDate:dd MMMM yyyy} to {endDate:dd MMMM yyyy}</p>
                        <p><strong>Generated on:</strong> {DateTime.Now:dd MMMM yyyy 'at' HH:mm}</p>
                    </div>");

            // Summary section
            sb.AppendLine($@"
                <div class='summary'>
                    <h3 style='color: #4E73DF; margin-top: 0;'>Report Summary</h3>
                    <p><strong>Total Claims:</strong> {claims.Count}</p>
                    <p><strong>Total Amount:</strong> {claims.Sum(c => c.Amount):C}</p>
                    <p><strong>Average Claim Amount:</strong> {(claims.Any() ? claims.Average(c => c.Amount) : 0):C}</p>
                </div>");

            // Claims table
            sb.AppendLine(@"
                <table>
                    <thead>
                        <tr>
                            <th>Claim ID</th>
                            <th>Lecturer Name</th>
                            <th>Department</th>
                            <th>Hours Worked</th>
                            <th>Amount</th>
                            <th>Status</th>
                        </tr>
                    </thead>
                    <tbody>");

            foreach (var claim in claims)
            {
                sb.AppendLine($@"
                        <tr>
                            <td>#{claim.ClaimID}</td>
                            <td>{claim.Lecturer?.Name ?? "N/A"}</td>
                            <td>{claim.Lecturer?.Department ?? "N/A"}</td>
                            <td>{claim.HoursWorked}</td>
                            <td>{claim.Amount:C}</td>
                            <td>{claim.Status}</td>
                        </tr>");
            }

            // Total row
            sb.AppendLine($@"
                        <tr class='total-row'>
                            <td colspan='4' style='text-align: right;'><strong>GRAND TOTAL</strong></td>
                            <td colspan='2'><strong>{claims.Sum(c => c.Amount):C}</strong></td>
                        </tr>");

            sb.AppendLine(@"
                    </tbody>
                </table>

                <div class='footer'>
                    <p>This report was automatically generated by the Contract Monthly Claim System.</p>
                    <p>For any inquiries, please contact the HR Department.</p>
                </div>

                </body>
                </html>");

            return sb.ToString();
        }

        private string EscapeCsvField(string field)
        {
            if (string.IsNullOrEmpty(field))
                return string.Empty;

            // Escape quotes and commas for CSV
            if (field.Contains('"') || field.Contains(',') || field.Contains('\n') || field.Contains('\r'))
            {
                return '"' + field.Replace("\"", "\"\"") + '"';
            }

            return field;
        }
    }
}