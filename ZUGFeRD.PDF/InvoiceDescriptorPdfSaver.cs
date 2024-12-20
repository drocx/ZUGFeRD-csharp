﻿/*
 * Licensed to the Apache Software Foundation (ASF) under one
 * or more contributor license agreements.  See the NOTICE file
 * distributed with this work for additional information
 * regarding copyright ownership.  The ASF licenses this file
 * to you under the Apache License, Version 2.0 (the
 * "License"); you may not use this file except in compliance
 * with the License.  You may obtain a copy of the License at
 * 
 *   http://www.apache.org/licenses/LICENSE-2.0
 * 
 * Unless required by applicable law or agreed to in writing,
 * software distributed under the License is distributed on an
 * "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY
 * KIND, either express or implied.  See the License for the
 * specific language governing permissions and limitations
 * under the License.
 */
using PdfSharp.Diagnostics;
using PdfSharp.Pdf.IO;
using PdfSharp.Pdf;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Resources;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;
using System.Xml;

namespace s2industries.ZUGFeRD.PDF
{
    internal class InvoiceDescriptorPdfSaver
    {
        internal static async Task SaveAsync(Stream targetStream, ZUGFeRDVersion version, Profile profile, ZUGFeRDFormats format, Stream pdfSourceStream, InvoiceDescriptor descriptor)
        {
            if (pdfSourceStream == null)
            {
                throw new ArgumentNullException("Invalid pdfSourceStream");
            }

            if (descriptor == null)
            {
                throw new ArgumentNullException("Invalid invoiceDescriptor");
            }

            MemoryStream xmlSourceStream = new MemoryStream();
            descriptor.Save(xmlSourceStream, version, profile, format);
            xmlSourceStream.Seek(0, SeekOrigin.Begin);

            Stream temp = _CreateFacturXStream(pdfSourceStream, xmlSourceStream, version, profile);
            await temp.CopyToAsync(targetStream);
        } // !SaveAsync()


        internal static async Task SaveAsync(string targetPath, ZUGFeRDVersion version, Profile profile, ZUGFeRDFormats format, string pdfSourcePath, InvoiceDescriptor descriptor)
        {
            if (!File.Exists(pdfSourcePath))
            {
                throw new FileNotFoundException("File not found", pdfSourcePath);
            }

            if (descriptor == null)
            {
                throw new ArgumentNullException("Invalid invoiceDescriptor");
            }

            FileStream pdfSourceStream = File.OpenRead(pdfSourcePath);
            MemoryStream targetStream = new MemoryStream();
            await SaveAsync(targetStream, version, profile, format, pdfSourceStream, descriptor);

            targetStream.Seek(0, SeekOrigin.Begin);
            System.IO.File.WriteAllBytes(targetPath, targetStream.ToArray());
        } // !SaveAsync()


        private static Stream _CreateFacturXStream(Stream pdfStream, Stream xmlStream, ZUGFeRDVersion version, Profile profile, string documentTitle = "Invoice", string documentDescription = "Invoice description", string invoiceFilename = "factur-x.xml")
        {
            if (pdfStream == null)
            {
                throw new ArgumentNullException(nameof(pdfStream));
            }

            if (xmlStream == null)
            {
                throw new ArgumentNullException(nameof(xmlStream));
            }

            var pdfDocument = PdfReader.Open(pdfStream, PdfDocumentOpenMode.Import);

            PdfDocument outputDocument = new PdfDocument();
            for (int i = 0; i < pdfDocument.PageCount; i++)
            {
                outputDocument.AddPage(pdfDocument.Pages[i]);
            }

            string xmlChecksum = string.Empty;
            byte[] xmlFileBytes = null;
            using (var md5 = MD5.Create())
            {
                var hashBytes = md5.ComputeHash(xmlStream);
                xmlChecksum = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();

                xmlStream.Seek(0, SeekOrigin.Begin);
                xmlFileBytes = new byte[xmlStream.Length];
                xmlStream.Read(xmlFileBytes, 0, (int)xmlStream.Length);
            }

            var xmlFileEncodedBytes = PdfSharp.Pdf.Filters.Filtering.FlateDecode.Encode(xmlFileBytes);

            PdfDictionary xmlParamsDict = new PdfDictionary();
            xmlParamsDict.Elements.Add("/CheckSum", new PdfString(xmlChecksum));
            xmlParamsDict.Elements.Add("/ModDate", new PdfString("D:" + DateTime.UtcNow.ToString("yyyyMMddHHmmsszzz")));
            xmlParamsDict.Elements.Add("/Size", new PdfInteger(xmlFileBytes.Length));



            PdfDictionary fStreamDict = new PdfDictionary();
            fStreamDict.CreateStream(xmlFileEncodedBytes);
            fStreamDict.Elements.Add("/Filter", new PdfName("/FlateDecode"));
            fStreamDict.Elements.Add("/Type", new PdfName("/EmbeddedFile"));
            fStreamDict.Elements.Add("/Params", xmlParamsDict);
            fStreamDict.Elements.Add("/Subtype", new PdfName("/text/xml"));
            outputDocument.Internals.AddObject(fStreamDict);


            PdfDictionary af0Dict = new PdfDictionary();
            af0Dict.Elements.Add("/AFRelationship", new PdfName("/Data"));
            af0Dict.Elements.Add("/Desc", new PdfString("Factur-X XML file"));
            af0Dict.Elements.Add("/Type", new PdfName("/Filespec"));
            af0Dict.Elements.Add("/F", new PdfString(invoiceFilename));
            PdfDictionary af1Dict = new PdfDictionary();
            af1Dict.Elements.Add("/F", fStreamDict.Reference);
            af1Dict.Elements.Add("/UF", fStreamDict.Reference);

            af0Dict.Elements.Add("/EF", af1Dict);
            af0Dict.Elements.Add("/UF", new PdfString(invoiceFilename));
            outputDocument.Internals.AddObject(af0Dict);

            var afPdfArray = new PdfArray();
            afPdfArray.Elements.Add(af0Dict.Reference);
            outputDocument.Internals.AddObject(afPdfArray);
            outputDocument.Internals.Catalog.Elements.Add("/AF", afPdfArray.Reference);


            var dateTimeNow = DateTime.UtcNow;
            var conformanceLevelName = profile.GetXMPName();

            string pdfMetadataTemplate = System.Text.Encoding.Default.GetString(_LoadEmbeddedResource("s2industries.ZUGFeRD.PDF.Resources.PdfMedatadataTemplate.xml"));
            var xmpmeta = pdfMetadataTemplate
                .Replace("{{InvoiceFilename}}", invoiceFilename)
                .Replace("{{CreationDate}}", dateTimeNow.ToString("yyyy-MM-ddThh:mm:sszzz"))
                .Replace("{{ModificationDate}}", dateTimeNow.ToString("yyyy-MM-ddThh:mm:sszzz"))
                .Replace("{{DocumentTitle}}", documentTitle)
                .Replace("{{DocumentDescription}}", documentDescription)
                .Replace("{{ConformanceLevel}}", conformanceLevelName);

            var metadataBytes = System.Text.Encoding.UTF8.GetBytes(xmpmeta);
            var metadataEncodedBytes = PdfSharp.Pdf.Filters.Filtering.FlateDecode.Encode(metadataBytes);

            PdfDictionary metadataDictionary = new PdfDictionary();
            metadataDictionary.CreateStream(metadataEncodedBytes);
            metadataDictionary.Elements.Add("/Filter", new PdfName("/FlateDecode"));
            metadataDictionary.Elements.Add("/Subtype", new PdfName("/XML"));
            metadataDictionary.Elements.Add("/Type", new PdfName("/Metadata"));
            outputDocument.Internals.AddObject(metadataDictionary);

            outputDocument.Internals.Catalog.Elements.Add("/Metadata", metadataDictionary.Reference);


            var namesPdfArray = new PdfArray();
            namesPdfArray.Elements.Add(new PdfString(invoiceFilename));
            namesPdfArray.Elements.Add(af0Dict.Reference);
            PdfDictionary embeddedFilesDict = new PdfDictionary();
            embeddedFilesDict.Elements.Add("/Names", namesPdfArray);
            PdfDictionary namesDict = new PdfDictionary();
            namesDict.Elements.Add("/EmbeddedFiles", embeddedFilesDict);

            outputDocument.Internals.Catalog.Elements.Add("/Names", namesDict);



            PdfDictionary rgbProfileDict = new PdfDictionary();
            rgbProfileDict.CreateStream(_LoadEmbeddedResource("s2industries.ZUGFeRD.PDF.Resources.sRGB-IEC61966-2.1.icc"));
            rgbProfileDict.Elements.Add("/N", new PdfInteger(3));
            outputDocument.Internals.AddObject(rgbProfileDict);

            PdfDictionary outputIntent0Dict = new PdfDictionary();
            outputIntent0Dict.Elements.Add("/DestOutputProfile", rgbProfileDict.Reference);
            outputIntent0Dict.Elements.Add("/OutputConditionIdentifier", new PdfString("sRGB IEC61966-2.1"));
            outputIntent0Dict.Elements.Add("/S", new PdfName("/GTS_PDFA1"));
            outputIntent0Dict.Elements.Add("/Type", new PdfName("/OutputIntent"));
            outputDocument.Internals.AddObject(outputIntent0Dict);

            var outputIntentsArray = new PdfArray();
            outputIntentsArray.Elements.Add(outputIntent0Dict.Reference);
            outputDocument.Internals.Catalog.Elements.Add("/OutputIntents", outputIntentsArray);

            outputDocument.Info.Creator = "S2 Industries";

            MemoryStream memoryStream = new MemoryStream();
            outputDocument.Save(memoryStream);
            memoryStream.Seek(0, SeekOrigin.Begin);
            return memoryStream;
        } // !_CreateFacturXStream()


        private static byte[] _LoadEmbeddedResource(string path)
        {
            var assembly = Assembly.GetExecutingAssembly();

            using (Stream stream = assembly.GetManifestResourceStream(path))
            {
                if (stream == null)
                {
                    return null;
                }

                using (var memoryStream = new MemoryStream())
                {
                    stream.CopyTo(memoryStream);
                    return memoryStream.ToArray();
                }
            }
        } // !_LoadEmbeddedResource()
    }
}
