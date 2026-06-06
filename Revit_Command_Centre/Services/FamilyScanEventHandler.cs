using System;
using System.Windows;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace Revit_Command_Centre.Services
{
    public record FamilyMetadata(
        string FilePath,
        string RevitCategoryName,
        string Manufacturer,
        string Model,
        string Description,
        string TypeComments);

    /// <summary>
    /// Opens a single .rfa on Revit's API thread, reads its category and identity
    /// parameters, closes the document, then posts the result back to the UI thread.
    /// </summary>
    public class FamilyScanEventHandler : IExternalEventHandler
    {
        public string?                 FilePath { get; set; }
        public Action<FamilyMetadata>? OnResult { get; set; }
        public Action<string>?         OnError  { get; set; }

        private static void PostToUi(Action action) =>
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                try { action(); }
                catch { /* don't propagate to Revit's dispatcher */ }
            });

        public void Execute(UIApplication app)
        {
            string? path = FilePath;
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                Document doc = app.Application.OpenDocumentFile(path);
                if (!doc.IsFamilyDocument)
                {
                    doc.Close(false);
                    PostToUi(() => OnError?.Invoke("Not a family document."));
                    return;
                }

                string catName  = doc.FamilyCategory?.Name ?? "";
                string mfr      = ReadParam(doc, BuiltInParameter.ALL_MODEL_MANUFACTURER);
                string model    = ReadParam(doc, BuiltInParameter.ALL_MODEL_MODEL);
                string desc     = ReadParam(doc, BuiltInParameter.ALL_MODEL_DESCRIPTION);
                string typeComm = ReadParam(doc, BuiltInParameter.ALL_MODEL_TYPE_COMMENTS);

                doc.Close(false);

                var meta = new FamilyMetadata(path, catName, mfr, model, desc, typeComm);
                PostToUi(() => OnResult?.Invoke(meta));
            }
            catch (Exception ex)
            {
                string err = ex.Message;
                PostToUi(() => OnError?.Invoke(err));
            }
        }

        private static string ReadParam(Document doc, BuiltInParameter bip)
        {
            FamilyManager? fm = doc.FamilyManager;
            if (fm == null) return "";

            // Try current type first, then iterate all types
            FamilyType? current = fm.CurrentType;
            if (current != null)
            {
                string val = StringFromParam(current.get_Parameter(bip));
                if (!string.IsNullOrEmpty(val)) return val;
            }
            foreach (FamilyType t in fm.Types)
            {
                string val = StringFromParam(t.get_Parameter(bip));
                if (!string.IsNullOrEmpty(val)) return val;
            }
            return "";
        }

        private static string StringFromParam(Parameter? p) =>
            p?.StorageType == StorageType.String ? p.AsString() ?? "" : "";

        public string GetName() => "BIM Command Centre — Scan Family";
    }
}
