﻿using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace LiteDB
{
    /// <summary>
    /// Represets a file inside storage collection
    /// </summary>
    public class LiteFileInfo
    {
        /// <summary>
        /// File id have a specific format - it's like file path.
        /// </summary>
        public const string ID_PATTERN = @"^[\w-$@!+%;\.]+(\/[\w-$@!+%;\.]+)*$";

        /// <summary>
        /// Number of bytes on each chunk document to store
        /// </summary>
        public const int CHUNK_SIZE = BsonDocument.MAX_DOCUMENT_SIZE - BasePage.PAGE_AVAILABLE_BYTES; // Chunk size is a page less than a max document size

        public string Id { get; private set; }
        public string Filename { get; set; }
        public string MimeType { get; set; }
        public long Length { get; private set; }
        public DateTime UploadDate { get; internal set; }
        public BsonObject Metadata { get; set; }

        private LiteDatabase _db;

        public LiteFileInfo(string id)
            : this(id, id)
        {
        }

        public LiteFileInfo(string id, string filename)
        {
            if (!Regex.IsMatch(id, ID_PATTERN)) throw new LiteException("Invalid file id format.");

            this.Id = id;
            this.Filename = Path.GetFileName(filename);
            this.MimeType = MimeTypeConverter.GetMimeType(this.Filename);
            this.Length = 0;
            this.UploadDate = DateTime.Now;
            this.Metadata = new BsonObject();
        }

        internal LiteFileInfo(LiteDatabase db, BsonDocument doc)
        {
            _db = db;

            this.Id = doc.Id.ToString();
            this.Filename = doc["filename"].AsString;
            this.MimeType = doc["mimeType"].AsString;
            this.Length = doc["length"].AsLong;
            this.UploadDate = doc["uploadDate"].AsDateTime;
            this.Metadata = doc["metadata"].AsObject;
        }

        public BsonDocument AsDocument
        {
            get
            {
                var doc = new BsonDocument();

                doc.Id = this.Id;
                doc["filename"] = this.Filename;
                doc["mimeType"] = this.MimeType;
                doc["length"] = this.Length;
                doc["uploadDate"] = this.UploadDate;
                doc["metadata"] = this.Metadata ?? new BsonObject();

                return doc;
            }
        }

        internal IEnumerable<BsonDocument> CreateChunks(Stream stream)
        {
            var buffer = new byte[CHUNK_SIZE];
            var read = 0;
            var index = 0;

            while ((read = stream.Read(buffer, 0, LiteFileInfo.CHUNK_SIZE)) > 0)
            {
                this.Length += (long)read;

                var chunk = new BsonDocument
                {
                    Id = GetChunckId(this.Id, index++) // index zero based
                };

                if (read != CHUNK_SIZE)
                {
                    var bytes = new byte[read];
                    Array.Copy(buffer, bytes, read);
                    chunk["data"] = bytes;
                }
                else
                {
                    chunk["data"] = buffer;
                }

                yield return chunk;
            }

            yield break;
        }

        /// <summary>
        /// Returns chunck Id for a file
        /// </summary>
        internal static string GetChunckId(string fileId, int index)
        {
            return string.Format("{0}\\{1:00000}", fileId, index);
        }

        /// <summary>
        /// Open file stream to read from database
        /// </summary>
        public LiteFileStream OpenRead()
        {
            if (_db == null) throw new LiteException("This FileEntry instance don't have reference to database");

            return new LiteFileStream(_db, this);
        }

        /// <summary>
        /// Save file content to a external file
        /// </summary>
        public void SaveAs(string filename, bool overwritten = true)
        {
            if (_db == null) throw new LiteException("This FileEntry instance don't have reference to database");

            using (var file = new FileStream(filename, overwritten ? FileMode.Create : FileMode.CreateNew))
            {
                this.OpenRead().CopyTo(file);
            }
        }
    }
}