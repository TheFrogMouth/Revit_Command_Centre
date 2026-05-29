using System;
using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using Revit_Command_Centre.Models;

namespace Revit_Command_Centre.Services
{
    /// <summary>
    /// Reads and writes ProjectConfig from/to Revit Extensible Storage on the
    /// active document's ProjectInformation element.  The schema is registered once
    /// per session; subsequent calls reuse the cached schema.
    /// </summary>
    public static class ExtensibleStorageService
    {
        private static readonly Guid SchemaGuid = new Guid("F2D3E4A5-B6C7-4D8E-9F0A-1B2C3D4E5F60");
        private const string SchemaName = "BIMCommandCentreProjectConfig";
        private const string VendorId = "FROGMOUTH";
        private const string FieldName = "ConfigJson";

        /// <summary>
        /// Returns an existing schema or builds and registers a new one.
        /// The schema stores a single Text field holding the serialised JSON config.
        /// </summary>
        private static Schema GetOrCreateSchema()
        {
            Schema? existing = Schema.Lookup(SchemaGuid);
            if (existing != null)
                return existing;

            SchemaBuilder builder = new SchemaBuilder(SchemaGuid);
            builder.SetSchemaName(SchemaName);
            builder.SetVendorId(VendorId);
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Vendor);
            builder.AddSimpleField(FieldName, typeof(string));

            return builder.Finish();
        }

        /// <summary>
        /// Serialises <paramref name="config"/> to JSON and stores it in extensible storage
        /// on the document's ProjectInformation element.
        /// Must be called inside an open Revit transaction.
        /// </summary>
        public static void WriteConfig(Document doc, ProjectConfig config)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (config == null) throw new ArgumentNullException(nameof(config));

            Schema schema = GetOrCreateSchema();
            Element projectInfo = doc.ProjectInformation;

            Entity entity = new Entity(schema);
            string json = JsonSerializer.Serialize(config);
            entity.Set(FieldName, json);

            projectInfo.SetEntity(entity);
        }

        /// <summary>
        /// Reads the stored ProjectConfig from the document's ProjectInformation element.
        /// Returns null if no config has been saved yet.
        /// </summary>
        public static ProjectConfig? ReadConfig(Document doc)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));

            Schema? schema = Schema.Lookup(SchemaGuid);
            if (schema == null)
                return null;

            Entity entity = doc.ProjectInformation.GetEntity(schema);
            if (!entity.IsValid())
                return null;

            string json = entity.Get<string>(FieldName);
            if (string.IsNullOrWhiteSpace(json))
                return null;

            return JsonSerializer.Deserialize<ProjectConfig>(json);
        }
    }
}
