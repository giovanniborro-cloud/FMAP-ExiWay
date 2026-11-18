using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;

namespace SchneiderLoader;

internal static class AddressWorkbook
{
    public static void Save(string path, IReadOnlyList<LampAddressStatus> addresses, bool includeLampStatus)
    {
        XNamespace sheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace links = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace relationships = "http://schemas.openxmlformats.org/package/2006/relationships";
        XNamespace types = "http://schemas.openxmlformats.org/package/2006/content-types";

        XElement Text(string value) => new(sheet + "c", new XAttribute("t", "inlineStr"),
            new XElement(sheet + "is", new XElement(sheet + "t", value)));
        var headers = new XElement(sheet + "row", Text("Linea"), Text("Numero"), Text("Indirizzo"), Text("Stato comunicazione"));
        if (includeLampStatus)
            headers.Add(Text("Guasti registrati"), Text("Modalità emergenza (codice)"), Text("Stato emergenza (codice)"));

        var data = new XElement(sheet + "sheetData", headers);
        foreach (var address in addresses)
        {
            var row = new XElement(sheet + "row",
                Text(address.Line.ToString()),
                new XElement(sheet + "c", new XElement(sheet + "v", address.Number.ToString(CultureInfo.InvariantCulture))),
                Text(address.Address),
                Text(address.CommunicationError ? "Errore segnalato" : "Nessun errore segnalato"));
            if (includeLampStatus)
                row.Add(Text(SchneiderProtocol.DescribeLampFaults(address.Faults)),
                    Text($"0x{address.EmergencyMode:X2}"), Text($"0x{address.EmergencyStatus:X2}"));
            data.Add(row);
        }

        string temporary = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, $".{Guid.NewGuid():N}.tmp");
        try
        {
            using (var archive = ZipFile.Open(temporary, ZipArchiveMode.Create))
            {
                void Add(string name, XElement xml)
                {
                    using var stream = archive.CreateEntry(name).Open();
                    xml.Save(stream);
                }

                XElement Relationship(string type, string target) => new(relationships + "Relationship",
                    new XAttribute("Id", "rId1"), new XAttribute("Type", links.NamespaceName + "/" + type),
                    new XAttribute("Target", target));
                XElement Override(string part, string contentType) => new(types + "Override",
                    new XAttribute("PartName", part), new XAttribute("ContentType", contentType));

                Add("[Content_Types].xml", new XElement(types + "Types",
                    new XElement(types + "Default", new XAttribute("Extension", "rels"),
                        new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
                    Override("/xl/workbook.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"),
                    Override("/xl/worksheets/sheet1.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")));
                Add("_rels/.rels", new XElement(relationships + "Relationships",
                    Relationship("officeDocument", "xl/workbook.xml")));
                Add("xl/workbook.xml", new XElement(sheet + "workbook",
                    new XElement(sheet + "sheets", new XElement(sheet + "sheet", new XAttribute("name", includeLampStatus ? "Stato lampade" : "Indirizzi"),
                        new XAttribute("sheetId", 1), new XAttribute(links + "id", "rId1")))));
                Add("xl/_rels/workbook.xml.rels", new XElement(relationships + "Relationships",
                    Relationship("worksheet", "worksheets/sheet1.xml")));
                XElement Column(int first, int last, int width) => new(sheet + "col",
                    new XAttribute("min", first), new XAttribute("max", last),
                    new XAttribute("width", width), new XAttribute("customWidth", 1));
                var columns = new XElement(sheet + "cols", Column(1, 3, 13), Column(4, 4, 28));
                if (includeLampStatus) columns.Add(Column(5, 5, 65), Column(6, 7, 32));
                Add("xl/worksheets/sheet1.xml", new XElement(sheet + "worksheet", columns, data));
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}
