using System.Globalization;
using System.IO.Compression;
using System.Text;
using csmhelper.Models;

namespace csmhelper.services
{
    /// <summary>
    /// Минимальный построитель XLSX без сторонних зависимостей.
    /// Формирует «роадмэп»: задачи в строках, дни в колонках, цветные полосы для задач,
    /// заливка для выходных и отпусков сотрудников.
    /// </summary>
    public static class RoadmapExporter
    {
        // Цвета баров по ролям — синхронизированы с UI (Views/Gant/Index.cshtml: ROLE_COLORS).
        private static readonly Dictionary<string, string> RoleColors = new()
        {
            ["analyst"]      = "FF96CEB4",
            ["backend_dev"]  = "FF4ECDC4",
            ["frontend_am"]  = "FF45B7D1",
            ["frontend_ao"]  = "FF7B5BD9",
            ["tester"]       = "FFFF6B6B",
        };
        private const string FallbackBar   = "FF95A5A6";
        private const string CriticalBar   = "FFDC3545";
        private const string WeekendFill   = "FFFFF3CD";
        private const string VacationFill  = "FFF8D7DA";
        private const string TodayBorder   = "FF0D6EFD";
        private const string HeaderFill    = "FFE8F0FB";

        // Стили в styles.xml — фиксированный массив индексов.
        // 0: default
        // 1: header  (bold, header bg, thin border)
        // 2: text cell (thin border, wrap)
        // 3: weekend cell
        // 4: vacation cell
        // 5: critical bar
        // 6: analyst bar
        // 7: backend bar
        // 8: frontend AM bar
        // 9: frontend AO bar
        // 10: tester bar
        // 11: fallback bar
        // 12: date header (bold, vertical)
        // 13: today highlight (header)
        private const int StyleDefault = 0;
        private const int StyleHeader = 1;
        private const int StyleText = 2;
        private const int StyleWeekend = 3;
        private const int StyleVacation = 4;
        private const int StyleCritical = 5;
        private const int StyleAnalyst = 6;
        private const int StyleBackend = 7;
        private const int StyleFrontAM = 8;
        private const int StyleFrontAO = 9;
        private const int StyleTester = 10;
        private const int StyleFallback = 11;
        private const int StyleDateHeader = 12;
        private const int StyleTodayHeader = 13;

        public static byte[] Build(List<GantScheduledTask> tasks, List<GantEmployeeInput> employees)
        {
            tasks ??= new List<GantScheduledTask>();
            employees ??= new List<GantEmployeeInput>();

            // Карты «исполнитель -> роль» и «исполнитель -> отпуска»
            var roleByName = employees
                .Where(e => !string.IsNullOrWhiteSpace(e.Name))
                .ToDictionary(e => e.Name.Trim(), e => (e.Role ?? "").ToLowerInvariant());

            var vacationsByName = employees
                .Where(e => !string.IsNullOrWhiteSpace(e.Name))
                .ToDictionary(
                    e => e.Name.Trim(),
                    e => (e.Vacations ?? new List<GantVacationInput>())
                         .Where(v => v.EndDate.Date >= v.StartDate.Date)
                         .Select(v => (Start: v.StartDate.Date, End: v.EndDate.Date))
                         .ToList());

            // Диапазон календаря — задачи и отпуска
            DateTime minDate, maxDate;
            if (tasks.Count > 0)
            {
                minDate = tasks.Min(t => t.ScheduledStart).Date;
                maxDate = tasks.Max(t => t.ScheduledEnd).Date;
            }
            else
            {
                minDate = DateTime.Today;
                maxDate = DateTime.Today.AddDays(7);
            }
            foreach (var lst in vacationsByName.Values)
                foreach (var v in lst)
                {
                    if (v.End >= minDate && v.Start <= maxDate)
                    {
                        if (v.Start < minDate) minDate = v.Start;
                        if (v.End > maxDate)   maxDate = v.End;
                    }
                }
            // Подстраховка от слишком большого/маленького окна
            var totalDays = (int)(maxDate - minDate).TotalDays + 1;
            if (totalDays < 1) totalDays = 1;
            if (totalDays > 366) totalDays = 366; // ограничиваем год — иначе xlsx становится тяжелым

            var today = DateTime.Today;

            // Подготовка колонок: фиксированных 8, далее — по дню
            // A:Key B:Summary C:Тип D:SP E:Исполнитель F:Начало G:Конец H:Дней I+:календарь
            const int fixedCols = 8;
            int firstCalendarCol = fixedCols + 1; // 1-based

            // Собираем содержимое sheet1.xml
            var sheet = new StringBuilder();
            sheet.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sheet.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");

            // sheetViews ОБЯЗАТЕЛЬНО должен идти ДО cols/sheetData по OOXML-схеме.
            sheet.Append("<sheetViews><sheetView workbookViewId=\"0\">");
            sheet.Append($"<pane xSplit=\"{fixedCols}\" ySplit=\"1\" topLeftCell=\"{ColLetters(firstCalendarCol)}2\" activePane=\"bottomRight\" state=\"frozen\"/>");
            sheet.Append("</sheetView></sheetViews>");

            // Описание ширин колонок: A — поуже, остальные — стандарт. Календарь — узкие.
            sheet.Append("<cols>");
            sheet.Append("<col min=\"1\" max=\"1\" width=\"12\" customWidth=\"1\"/>");
            sheet.Append("<col min=\"2\" max=\"2\" width=\"40\" customWidth=\"1\"/>");
            sheet.Append("<col min=\"3\" max=\"3\" width=\"12\" customWidth=\"1\"/>");
            sheet.Append("<col min=\"4\" max=\"4\" width=\"6\"  customWidth=\"1\"/>");
            sheet.Append("<col min=\"5\" max=\"5\" width=\"28\" customWidth=\"1\"/>");
            sheet.Append("<col min=\"6\" max=\"7\" width=\"12\" customWidth=\"1\"/>");
            sheet.Append("<col min=\"8\" max=\"8\" width=\"7\"  customWidth=\"1\"/>");
            sheet.Append($"<col min=\"{firstCalendarCol}\" max=\"{firstCalendarCol + totalDays - 1}\" width=\"4\" customWidth=\"1\"/>");
            sheet.Append("</cols>");

            sheet.Append("<sheetData>");

            // ── Header row ──
            sheet.Append("<row r=\"1\" ht=\"60\" customHeight=\"1\">");
            AppendInlineStr(sheet, "A1", "Ключ",       StyleHeader);
            AppendInlineStr(sheet, "B1", "Название",   StyleHeader);
            AppendInlineStr(sheet, "C1", "Тип",        StyleHeader);
            AppendInlineStr(sheet, "D1", "SP",         StyleHeader);
            AppendInlineStr(sheet, "E1", "Исполнитель",StyleHeader);
            AppendInlineStr(sheet, "F1", "Начало",     StyleHeader);
            AppendInlineStr(sheet, "G1", "Конец",      StyleHeader);
            AppendInlineStr(sheet, "H1", "Дней",       StyleHeader);
            for (int i = 0; i < totalDays; i++)
            {
                var d = minDate.AddDays(i);
                var cellRef = ColLetters(firstCalendarCol + i) + "1";
                bool isToday = d.Date == today;
                AppendInlineStr(sheet, cellRef, d.ToString("dd.MM", CultureInfo.InvariantCulture),
                    isToday ? StyleTodayHeader : StyleDateHeader);
            }
            sheet.Append("</row>");

            // ── Data rows ──
            // Сортируем задачи по дате начала для читабельного роадмэпа
            var ordered = tasks.OrderBy(t => t.ScheduledStart).ToList();
            int rowIdx = 2;
            foreach (var task in ordered)
            {
                int barStyle = ResolveBarStyle(task, roleByName);
                var vacationsForAssignee = !string.IsNullOrEmpty(task.AssignedResource) &&
                                           vacationsByName.TryGetValue(task.AssignedResource!, out var vacList)
                    ? vacList
                    : new List<(DateTime Start, DateTime End)>();

                sheet.Append($"<row r=\"{rowIdx}\">");
                AppendInlineStr(sheet, $"A{rowIdx}", task.Key ?? "", StyleText);
                AppendInlineStr(sheet, $"B{rowIdx}", task.Summary ?? "", StyleText);
                AppendInlineStr(sheet, $"C{rowIdx}", task.TaskType ?? "", StyleText);
                AppendNumber  (sheet, $"D{rowIdx}", task.StoryPoints, StyleText);
                AppendInlineStr(sheet, $"E{rowIdx}", task.AssignedResource ?? "—", StyleText);
                AppendInlineStr(sheet, $"F{rowIdx}", task.ScheduledStart.ToString("dd.MM.yyyy HH:mm"), StyleText);
                AppendInlineStr(sheet, $"G{rowIdx}", task.ScheduledEnd.ToString("dd.MM.yyyy HH:mm"), StyleText);
                AppendNumber  (sheet, $"H{rowIdx}", Math.Round(task.DurationWorkDays, 1), StyleText);

                var startD = task.ScheduledStart.Date;
                var endD   = task.ScheduledEnd.Date;

                for (int i = 0; i < totalDays; i++)
                {
                    var d = minDate.AddDays(i);
                    var cellRef = ColLetters(firstCalendarCol + i) + rowIdx;

                    bool isWeekend = d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
                    bool isVacation = vacationsForAssignee.Any(v => d >= v.Start && d <= v.End);
                    bool inTask = d >= startD && d <= endD;

                    // Приоритет визуализации: бар задачи > отпуск > выходной
                    int style;
                    string content = "";
                    if (inTask)
                    {
                        style = barStyle;
                    }
                    else if (isVacation)
                    {
                        style = StyleVacation;
                    }
                    else if (isWeekend)
                    {
                        style = StyleWeekend;
                    }
                    else
                    {
                        // Не пишем пустые ячейки совсем, экономим место
                        continue;
                    }

                    if (string.IsNullOrEmpty(content))
                        sheet.Append($"<c r=\"{cellRef}\" s=\"{style}\"/>");
                    else
                        AppendInlineStr(sheet, cellRef, content, style);
                }
                sheet.Append("</row>");
                rowIdx++;
            }

            sheet.Append("</sheetData>");

            sheet.Append("</worksheet>");

            // Собираем все части и пакуем в ZIP
            using var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                WriteEntry(zip, "[Content_Types].xml", ContentTypesXml());
                WriteEntry(zip, "_rels/.rels", RootRelsXml());
                WriteEntry(zip, "xl/workbook.xml", WorkbookXml());
                WriteEntry(zip, "xl/_rels/workbook.xml.rels", WorkbookRelsXml());
                WriteEntry(zip, "xl/styles.xml", StylesXml());
                WriteEntry(zip, "xl/worksheets/sheet1.xml", sheet.ToString());
            }
            return ms.ToArray();
        }

        // ── Helpers ────────────────────────────────────────────────────

        private static int ResolveBarStyle(GantScheduledTask task, Dictionary<string, string> roleByName)
        {
            if (task.IsCritical) return StyleCritical;
            if (string.IsNullOrEmpty(task.AssignedResource)) return StyleFallback;
            if (!roleByName.TryGetValue(task.AssignedResource!, out var role)) return StyleFallback;
            return role switch
            {
                "analyst"      => StyleAnalyst,
                "backend_dev"  => StyleBackend,
                "frontend_am"  => StyleFrontAM,
                "frontend_ao"  => StyleFrontAO,
                "tester"       => StyleTester,
                _              => StyleFallback,
            };
        }

        private static void AppendInlineStr(StringBuilder sb, string cellRef, string text, int style)
        {
            sb.Append($"<c r=\"{cellRef}\" s=\"{style}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">");
            sb.Append(Escape(text));
            sb.Append("</t></is></c>");
        }

        private static void AppendNumber(StringBuilder sb, string cellRef, double value, int style)
        {
            sb.Append($"<c r=\"{cellRef}\" s=\"{style}\"><v>");
            sb.Append(value.ToString("0.##", CultureInfo.InvariantCulture));
            sb.Append("</v></c>");
        }

        private static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;");
        }

        private static string ColLetters(int col1Based)
        {
            // 1 -> A, 27 -> AA
            var sb = new StringBuilder();
            int n = col1Based;
            while (n > 0)
            {
                int r = (n - 1) % 26;
                sb.Insert(0, (char)('A' + r));
                n = (n - 1) / 26;
            }
            return sb.ToString();
        }

        private static void WriteEntry(ZipArchive zip, string path, string content)
        {
            var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
            using var s = entry.Open();
            var bytes = Encoding.UTF8.GetBytes(content);
            s.Write(bytes, 0, bytes.Length);
        }

        // ── Static XML parts ───────────────────────────────────────────

        private static string ContentTypesXml() =>
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
            "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
            "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
            "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
            "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
            "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
            "</Types>";

        private static string RootRelsXml() =>
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
            "</Relationships>";

        private static string WorkbookXml() =>
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" " +
            "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
            "<sheets><sheet name=\"Roadmap\" sheetId=\"1\" r:id=\"rId1\"/></sheets>" +
            "</workbook>";

        private static string WorkbookRelsXml() =>
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
            "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
            "</Relationships>";

        private static string StylesXml()
        {
            // Цвета заливок (по индексам, начиная с 2 — первые две зарезервированы спецификацией)
            // 0: none, 1: gray125 (required defaults)
            // 2: header fill
            // 3: weekend
            // 4: vacation
            // 5: critical
            // 6: analyst
            // 7: backend
            // 8: frontend AM
            // 9: frontend AO
            // 10: tester
            // 11: fallback
            // 12: today header
            var fills = new[]
            {
                "<fill><patternFill patternType=\"none\"/></fill>",
                "<fill><patternFill patternType=\"gray125\"/></fill>",
                $"<fill><patternFill patternType=\"solid\"><fgColor rgb=\"{HeaderFill}\"/></patternFill></fill>",
                $"<fill><patternFill patternType=\"solid\"><fgColor rgb=\"{WeekendFill}\"/></patternFill></fill>",
                $"<fill><patternFill patternType=\"solid\"><fgColor rgb=\"{VacationFill}\"/></patternFill></fill>",
                $"<fill><patternFill patternType=\"solid\"><fgColor rgb=\"{CriticalBar}\"/></patternFill></fill>",
                $"<fill><patternFill patternType=\"solid\"><fgColor rgb=\"{RoleColors["analyst"]}\"/></patternFill></fill>",
                $"<fill><patternFill patternType=\"solid\"><fgColor rgb=\"{RoleColors["backend_dev"]}\"/></patternFill></fill>",
                $"<fill><patternFill patternType=\"solid\"><fgColor rgb=\"{RoleColors["frontend_am"]}\"/></patternFill></fill>",
                $"<fill><patternFill patternType=\"solid\"><fgColor rgb=\"{RoleColors["frontend_ao"]}\"/></patternFill></fill>",
                $"<fill><patternFill patternType=\"solid\"><fgColor rgb=\"{RoleColors["tester"]}\"/></patternFill></fill>",
                $"<fill><patternFill patternType=\"solid\"><fgColor rgb=\"{FallbackBar}\"/></patternFill></fill>",
                $"<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFD0E2FF\"/></patternFill></fill>", // today
            };

            // Fonts
            var fonts = new[]
            {
                "<font><sz val=\"11\"/><name val=\"Calibri\"/></font>",          // 0 default
                "<font><b/><sz val=\"11\"/><name val=\"Calibri\"/></font>",      // 1 bold
                "<font><b/><sz val=\"10\"/><color rgb=\"FFFFFFFF\"/><name val=\"Calibri\"/></font>", // 2 bold white
            };

            // Borders
            var borders = new[]
            {
                "<border><left/><right/><top/><bottom/><diagonal/></border>", // 0 none
                "<border>" +
                    "<left style=\"thin\"><color rgb=\"FFCCCCCC\"/></left>" +
                    "<right style=\"thin\"><color rgb=\"FFCCCCCC\"/></right>" +
                    "<top style=\"thin\"><color rgb=\"FFCCCCCC\"/></top>" +
                    "<bottom style=\"thin\"><color rgb=\"FFCCCCCC\"/></bottom>" +
                "<diagonal/></border>", // 1 thin grey
                "<border>" +
                    $"<left style=\"medium\"><color rgb=\"{TodayBorder}\"/></left>" +
                    $"<right style=\"medium\"><color rgb=\"{TodayBorder}\"/></right>" +
                    "<top/><bottom/><diagonal/></border>", // 2 today bold side borders
            };

            // cellXfs (индекс ↔ роль/назначение):
            // 0 default
            // 1 header           (font=1 fill=2 border=1, center)
            // 2 text border      (font=0 fill=0 border=1, wrap)
            // 3 weekend          (font=0 fill=3 border=1)
            // 4 vacation         (font=0 fill=4 border=1)
            // 5 critical bar     (font=2 fill=5 border=1)
            // 6 analyst bar      (font=2 fill=6 border=1)
            // 7 backend bar      (font=2 fill=7 border=1)
            // 8 frontend AM bar  (font=2 fill=8 border=1)
            // 9 frontend AO bar  (font=2 fill=9 border=1)
            // 10 tester bar      (font=2 fill=10 border=1)
            // 11 fallback bar    (font=2 fill=11 border=1)
            // 12 date header     (font=1 fill=2 border=1, rotated)
            // 13 today header    (font=1 fill=12 border=2, rotated)
            var cellXfs = new[]
            {
                "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/>",
                "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"2\" borderId=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"center\" wrapText=\"1\"/></xf>",
                "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"1\" applyAlignment=\"1\"><alignment vertical=\"center\" wrapText=\"1\"/></xf>",
                "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"3\" borderId=\"1\"/>",
                "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"4\" borderId=\"1\"/>",
                "<xf numFmtId=\"0\" fontId=\"2\" fillId=\"5\" borderId=\"1\"/>",
                "<xf numFmtId=\"0\" fontId=\"2\" fillId=\"6\" borderId=\"1\"/>",
                "<xf numFmtId=\"0\" fontId=\"2\" fillId=\"7\" borderId=\"1\"/>",
                "<xf numFmtId=\"0\" fontId=\"2\" fillId=\"8\" borderId=\"1\"/>",
                "<xf numFmtId=\"0\" fontId=\"2\" fillId=\"9\" borderId=\"1\"/>",
                "<xf numFmtId=\"0\" fontId=\"2\" fillId=\"10\" borderId=\"1\"/>",
                "<xf numFmtId=\"0\" fontId=\"2\" fillId=\"11\" borderId=\"1\"/>",
                "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"2\" borderId=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"center\" textRotation=\"90\"/></xf>",
                "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"12\" borderId=\"2\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"center\" textRotation=\"90\"/></xf>",
            };

            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.Append("<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
            sb.Append($"<fonts count=\"{fonts.Length}\">"); foreach (var f in fonts) sb.Append(f); sb.Append("</fonts>");
            sb.Append($"<fills count=\"{fills.Length}\">"); foreach (var f in fills) sb.Append(f); sb.Append("</fills>");
            sb.Append($"<borders count=\"{borders.Length}\">"); foreach (var b in borders) sb.Append(b); sb.Append("</borders>");
            sb.Append("<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>");
            sb.Append($"<cellXfs count=\"{cellXfs.Length}\">"); foreach (var x in cellXfs) sb.Append(x); sb.Append("</cellXfs>");
            sb.Append("<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>");
            sb.Append("</styleSheet>");
            return sb.ToString();
        }
    }
}
