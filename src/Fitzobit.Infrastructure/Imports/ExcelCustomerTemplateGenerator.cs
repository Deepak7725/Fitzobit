using ClosedXML.Excel;
using Fitzobit.Application.Imports;
using Fitzobit.Application.Imports.Validation;

namespace Fitzobit.Infrastructure.Imports;

/// <summary>
/// Builds the downloadable customer template: a Customers sheet (headers, clearly marked sample rows, text-formatted
/// ID/phone columns, plan drop-down), a Plans sheet generated from the database, and an Instructions sheet.
/// </summary>
public sealed class ExcelCustomerTemplateGenerator : ICustomerTemplateGenerator
{
    private const int FormattedRows = 5000;
    private static readonly XLColor Navy = XLColor.FromHtml("#0E1D3D");
    private static readonly XLColor Blue = XLColor.FromHtml("#2563EB");
    private static readonly XLColor SampleFill = XLColor.FromHtml("#FFF7E0");

    private static readonly (string Header, string Format, string Example, string Notes)[] Columns =
    [
        (CustomerFileHeaders.CustomerId, "Text, unique", "CUST1001", "Your own customer code. Must be unique in the file. Existing IDs are matched, never overwritten."),
        (CustomerFileHeaders.CustomerName, "Text", "Customer full name", "2–150 characters."),
        (CustomerFileHeaders.PhoneNumber, "Text, 10 digits", "9XXXXXXXXX", "Indian mobile number. +91 / 0 prefixes are accepted. Keep the column formatted as Text."),
        (CustomerFileHeaders.Location, "Text", "Door no, Street, Area, City - Pincode", "Full delivery address. Include the 6-digit pincode so customers can be grouped."),
        (CustomerFileHeaders.Plan, "Text", "A plan name from the Plans sheet", "Choose from the drop-down or type a plan name. Unknown plans are flagged for review."),
    ];

    public byte[] Generate(IReadOnlyList<TemplatePlan> plans)
    {
        using var workbook = new XLWorkbook();
        var customers = workbook.AddWorksheet("Customers");
        var instructions = workbook.AddWorksheet("Instructions");
        var planSheet = workbook.AddWorksheet("Plans");

        BuildPlans(planSheet, plans);
        BuildCustomers(customers, plans, planSheet);
        BuildInstructions(instructions);

        customers.SetTabActive();
        customers.SetTabColor(Blue);
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void BuildCustomers(IXLWorksheet sheet, IReadOnlyList<TemplatePlan> plans, IXLWorksheet planSheet)
    {
        for (var i = 0; i < Columns.Length; i++)
        {
            var cell = sheet.Cell(1, i + 1);
            cell.Value = Columns[i].Header;
            cell.CreateComment().AddText($"Required. {Columns[i].Notes}");
        }
        StyleHeader(sheet.Range(1, 1, 1, Columns.Length));

        // Keep IDs and phone numbers as text so Excel never turns them into numbers or scientific notation.
        sheet.Range(2, 1, FormattedRows + 1, 1).Style.NumberFormat.Format = "@";
        sheet.Range(2, 3, FormattedRows + 1, 3).Style.NumberFormat.Format = "@";

        var samplePlans = plans.Count == 0 ? ["(choose a plan)"] : plans.Select(p => p.Name).Take(3).ToArray();
        var samples = new[]
        {
            ($"{CustomerRowValidator.SamplePrefix}001", "Sample Customer A", "9000000001", "12 Example Street, RS Puram, Coimbatore - 641002"),
            ($"{CustomerRowValidator.SamplePrefix}002", "Sample Customer B", "9000000002", "45 Sample Road, Anna Nagar, Chennai - 600040"),
            ($"{CustomerRowValidator.SamplePrefix}003", "Sample Customer C", "9000000003", "7 Demo Lane, Medical College Road, Thanjavur - 613004"),
        };
        for (var i = 0; i < samples.Length; i++)
        {
            var row = i + 2;
            sheet.Cell(row, 1).SetValue(samples[i].Item1);
            sheet.Cell(row, 2).SetValue(samples[i].Item2);
            sheet.Cell(row, 3).SetValue(samples[i].Item3);
            sheet.Cell(row, 4).SetValue(samples[i].Item4);
            sheet.Cell(row, 5).SetValue(samplePlans[i % samplePlans.Length]);
        }
        var sampleRange = sheet.Range(2, 1, samples.Length + 1, Columns.Length);
        sampleRange.Style.Fill.BackgroundColor = SampleFill;
        sampleRange.Style.Font.Italic = true;
        sampleRange.Style.Font.FontColor = XLColor.FromHtml("#7A5B00");
        sheet.Cell(2, 1).CreateComment().AddText("Sample rows (SAMPLE-…) show the format only. Delete them before uploading; they are rejected on import.");

        if (plans.Count > 0)
        {
            var validation = sheet.Range(2, 5, FormattedRows + 1, 5).CreateDataValidation();
            validation.List(planSheet.Range(2, 1, plans.Count + 1, 1), true);
            validation.ErrorStyle = XLErrorStyle.Information; // suggest, but allow other text (it is checked on upload)
            validation.ErrorTitle = "Plan not in the list";
            validation.ErrorMessage = "This text is not one of the Fitzobit plans. It will be flagged for review on upload.";
            validation.InputTitle = "Plan";
            validation.InputMessage = "Pick a Fitzobit plan from the list.";
        }

        sheet.Column(1).Width = 16;
        sheet.Column(2).Width = 28;
        sheet.Column(3).Width = 16;
        sheet.Column(4).Width = 58;
        sheet.Column(5).Width = 52;
        sheet.Range(1, 1, samples.Length + 1, Columns.Length).SetAutoFilter();
        sheet.SheetView.FreezeRows(1);
    }

    private static void BuildPlans(IXLWorksheet sheet, IReadOnlyList<TemplatePlan> plans)
    {
        sheet.Cell(1, 1).Value = "Plan (enter exactly)";
        sheet.Cell(1, 2).Value = "Category";
        sheet.Cell(1, 3).Value = "Variants (optional: add after the plan name, e.g. \"… Veg\")";
        StyleHeader(sheet.Range(1, 1, 1, 3));

        for (var i = 0; i < plans.Count; i++)
        {
            sheet.Cell(i + 2, 1).SetValue(plans[i].Name);
            sheet.Cell(i + 2, 2).SetValue(plans[i].Category ?? "");
            sheet.Cell(i + 2, 3).SetValue(plans[i].Variants ?? "");
        }

        sheet.Column(1).Width = 55;
        sheet.Column(2).Width = 22;
        sheet.Column(3).Width = 70;
        sheet.Range(2, 3, Math.Max(2, plans.Count + 1), 3).Style.Alignment.WrapText = true;
        sheet.Range(1, 1, Math.Max(1, plans.Count + 1), 3).SetAutoFilter();
        sheet.SheetView.FreezeRows(1);
    }

    private static void BuildInstructions(IXLWorksheet sheet)
    {
        sheet.Cell(1, 1).Value = "Fitzobit customer upload: instructions";
        sheet.Cell(1, 1).Style.Font.Bold = true;
        sheet.Cell(1, 1).Style.Font.FontSize = 16;
        sheet.Cell(1, 1).Style.Font.FontColor = Navy;

        var steps = new[]
        {
            "1. Enter one customer per row on the \"Customers\" sheet, starting on row 2.",
            "2. All five columns are required. Do not rename or repeat the header row.",
            "3. Delete the yellow SAMPLE-… rows before uploading (they are rejected on import).",
            "4. Keep CustomerID and PhoneNumber formatted as Text so Excel does not change the digits.",
            "5. Save as Excel Workbook (.xlsx). Macro-enabled (.xlsm) and old .xls files are not accepted. Maximum 10 MB / 20,000 rows.",
            "6. Upload the file in Fitzobit → Upload & Create Groups. Rows with problems are listed so you can fix and re-upload, or continue with the valid rows.",
        };
        for (var i = 0; i < steps.Length; i++) sheet.Cell(3 + i, 1).Value = steps[i];

        var top = 4 + steps.Length;
        string[] headers = ["Column", "Required", "Format", "Example", "Notes"];
        for (var c = 0; c < headers.Length; c++) sheet.Cell(top, c + 1).Value = headers[c];
        StyleHeader(sheet.Range(top, 1, top, headers.Length));

        for (var i = 0; i < Columns.Length; i++)
        {
            var r = top + 1 + i;
            sheet.Cell(r, 1).Value = Columns[i].Header;
            sheet.Cell(r, 2).Value = "Yes";
            sheet.Cell(r, 3).Value = Columns[i].Format;
            sheet.Cell(r, 4).Value = Columns[i].Example;
            sheet.Cell(r, 5).Value = Columns[i].Notes;
            sheet.Cell(r, 1).Style.Font.Bold = true;
            sheet.Cell(r, 2).Style.Font.FontColor = XLColor.FromHtml("#B91C1C");
            sheet.Cell(r, 2).Style.Font.Bold = true;
        }

        var table = sheet.Range(top, 1, top + Columns.Length, headers.Length);
        table.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        table.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        table.Style.Border.OutsideBorderColor = XLColor.FromHtml("#D3DAE6");
        table.Style.Border.InsideBorderColor = XLColor.FromHtml("#D3DAE6");
        table.Style.Alignment.WrapText = true;
        table.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;

        sheet.Column(1).Width = 18;
        sheet.Column(2).Width = 10;
        sheet.Column(3).Width = 18;
        sheet.Column(4).Width = 34;
        sheet.Column(5).Width = 70;
    }

    private static void StyleHeader(IXLRange range)
    {
        range.Style.Fill.BackgroundColor = Navy;
        range.Style.Font.FontColor = XLColor.White;
        range.Style.Font.Bold = true;
        range.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        range.Style.Alignment.WrapText = true;
        range.Worksheet.Row(range.FirstRow().RowNumber()).Height = 24;
    }
}
