﻿using Com.DanLiris.Service.Purchasing.Lib.Helpers;
using Com.DanLiris.Service.Purchasing.Lib.ViewModels.Master;
using Com.DanLiris.Service.Purchasing.Lib.ViewModels.UnitReceiptNote;
using MongoDB.Bson;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;

namespace Com.DanLiris.Service.Purchasing.Lib.Facades.Report
{
    public class ImportPurchasingBookReportFacade
    {
        public Tuple<List<UnitReceiptNoteViewModel>, int> GetReport(string no, string unit, string category, DateTime? dateFrom, DateTime? dateTo)
        {
            IMongoCollection<BsonDocument> collection = new MongoDbContext().UnitReceiptNote;
            IMongoCollection<BsonDocument> collectionUnitPaymentOrder = new MongoDbContext().UnitPaymentOrder;

            FilterDefinitionBuilder<BsonDocument> filterBuilder = Builders<BsonDocument>.Filter;
            List<FilterDefinition<BsonDocument>> filter = new List<FilterDefinition<BsonDocument>>
            {
                filterBuilder.Eq("_deleted", false),
                filterBuilder.Eq("supplier.import", true)
            };

            if (no != null)
                filter.Add(filterBuilder.Eq("no", no));
            if (unit != null)
                filter.Add(filterBuilder.Eq("unit.code", unit));
            if (category != null)
                filter.Add(filterBuilder.Eq("items.purchaseOrder.category.code", category));
            if (dateFrom != null && dateTo != null)
                filter.Add(filterBuilder.And(filterBuilder.Gte("date", dateFrom), filterBuilder.Lte("date", dateTo)));

            List<BsonDocument> ListData = collection.Aggregate()
                .Match(filterBuilder.And(filter))
                .ToList();

            List<UnitReceiptNoteViewModel> Data = new List<UnitReceiptNoteViewModel>();

            foreach (var data in ListData)
            {
                List<UnitReceiptNoteItemViewModel> Items = new List<UnitReceiptNoteItemViewModel>();
                foreach (var item in data.GetValue("items").AsBsonArray)
                {
                    var itemDocument = item.AsBsonDocument;
                    Items.Add(new UnitReceiptNoteItemViewModel
                    {
                        DeliveredQuantity = GetDoubleValue(itemDocument, "deliveredQuantity"),
                        PricePerDealUnit = GetDoubleValue(itemDocument, "pricePerDealUnit"),
                        CurrencyRate = GetDoubleValue(itemDocument, "currencyRate"),
                        Product = new ProductViewModel
                        {
                            Name = GetStringValue(itemDocument, "product.name")
                        },
                        PurchaseOrder = new PurchaseOrderViewModel
                        {
                            Category = new CategoryViewModel
                            {
                                Name = GetStringValue(itemDocument, "purchaseOrder.category.code")
                            }
                        },
                    });
                }
                var UnitReceiptNoteNo = GetStringValue(data, "no");
                var dataUnitPaymentOrder = collectionUnitPaymentOrder.Find(filterBuilder.Eq("items.unitReceiptNote.no", UnitReceiptNoteNo)).FirstOrDefault();
                Data.Add(new UnitReceiptNoteViewModel
                {
                    No = UnitReceiptNoteNo,
                    Date = data.GetValue("date").ToUniversalTime(),
                    Unit = new UnitViewModel
                    {
                        Name = GetStringValue(data, "unit.name")
                    },
                    PIB = dataUnitPaymentOrder != null ? GetStringValue(dataUnitPaymentOrder, "pibNo", new BsonString("-")) : "-",
                    UnitReceiptNoteItems = Items,
                });
            }

            return Tuple.Create(Data, Data.Count);
        }

        public MemoryStream GenerateExcel(string no, string unit, string category, DateTime? dateFrom, DateTime? dateTo)
        {
            Tuple<List<UnitReceiptNoteViewModel>, int> Data = this.GetReport(no, unit, category, dateFrom, dateTo);

            DataTable result = new DataTable();
            result.Columns.Add(new DataColumn() { ColumnName = "TGL", DataType = typeof(String) });
            result.Columns.Add(new DataColumn() { ColumnName = "NOMOR NOTA", DataType = typeof(String) });
            result.Columns.Add(new DataColumn() { ColumnName = "NAMA BARANG", DataType = typeof(String) });
            result.Columns.Add(new DataColumn() { ColumnName = "TIPE", DataType = typeof(String) });
            result.Columns.Add(new DataColumn() { ColumnName = "UNIT", DataType = typeof(String) });
            result.Columns.Add(new DataColumn() { ColumnName = "NO PIB", DataType = typeof(String) });
            result.Columns.Add(new DataColumn() { ColumnName = "NILAI", DataType = typeof(double) });
            result.Columns.Add(new DataColumn() { ColumnName = "RATE", DataType = typeof(double) });
            result.Columns.Add(new DataColumn() { ColumnName = "TOTAL", DataType = typeof(double) });

            List<(string, Enum, Enum)> mergeCells = new List<(string, Enum, Enum)>() { };

            if (Data.Item2 == 0)
            {
                result.Rows.Add("", "", "", "", "", "", 0, 0, 0); // to allow column name to be generated properly for empty data as template
            }
            else
            {
                Dictionary<string, List<UnitReceiptNoteViewModel>> dataByCategory = new Dictionary<string, List<UnitReceiptNoteViewModel>>();
                Dictionary<string, double> subTotalCategory = new Dictionary<string, double>();

                foreach (UnitReceiptNoteViewModel data in Data.Item1)
                {
                    foreach (UnitReceiptNoteItemViewModel item in data.UnitReceiptNoteItems)
                    {
                        string categoryName = item.PurchaseOrder.Category.Name;

                        if (!dataByCategory.ContainsKey(categoryName)) dataByCategory.Add(categoryName, new List<UnitReceiptNoteViewModel> { });
                        dataByCategory[categoryName].Add(new UnitReceiptNoteViewModel
                        {
                            No = data.No,
                            Date = data.Date,
                            PIB = data.PIB,
                            Unit = data.Unit,
                            UnitReceiptNoteItems = new List<UnitReceiptNoteItemViewModel>() { item }
                        });

                        if (!subTotalCategory.ContainsKey(categoryName)) subTotalCategory.Add(categoryName, 0);
                        subTotalCategory[categoryName] += item.DeliveredQuantity;
                    }
                }

                double total = 0;
                int rowPosition = 1;

                foreach (KeyValuePair<string, List<UnitReceiptNoteViewModel>> categoryName in dataByCategory)
                {
                    foreach (UnitReceiptNoteViewModel data in categoryName.Value)
                    {
                        UnitReceiptNoteItemViewModel item = data.UnitReceiptNoteItems[0];
                        result.Rows.Add(data.Date.ToShortDateString(), data.No, item.Product.Name, item.PurchaseOrder.Category.Name, data.Unit.Name, data.PIB, item.PricePerDealUnit * item.DeliveredQuantity, item.CurrencyRate, item.PricePerDealUnit * item.DeliveredQuantity * item.CurrencyRate);
                        rowPosition += 1;
                    }
                    result.Rows.Add("SUB TOTAL", "", "", "", "", "", 0, 0, subTotalCategory[categoryName.Key]);
                    rowPosition += 1;

                    mergeCells.Add(($"A{rowPosition}:H{rowPosition}", OfficeOpenXml.Style.ExcelHorizontalAlignment.Right, OfficeOpenXml.Style.ExcelVerticalAlignment.Bottom));

                    total += subTotalCategory[categoryName.Key];
                }
                result.Rows.Add("TOTAL", "", "", "", "", "", 0, 0, total);
                rowPosition += 1;

                mergeCells.Add(($"A{rowPosition}:H{rowPosition}", OfficeOpenXml.Style.ExcelHorizontalAlignment.Right, OfficeOpenXml.Style.ExcelVerticalAlignment.Bottom));
            }

            return Excel.CreateExcel(new List<(DataTable, string, List<(string, Enum, Enum)>)>() { (result, "Report", mergeCells) }, true);
        }

        // JSON ora iso nge-cast
        public Tuple<List<BsonDocument>, int> GetReport()
        {
            IMongoCollection<BsonDocument> collection = new MongoDbContext().UnitReceiptNote;
            List<BsonDocument> ListData = collection.Aggregate().ToList();

            return Tuple.Create(ListData, ListData.Count);
        }

        string GetStringValue(BsonDocument bsonDocument, string field, BsonString bsonString)
        {
            BsonValue bsonValue;
            string[] fields = field.Split(".");

            bsonValue = fields.Length > 1 ?
                bsonDocument.GetValue(fields[0], new BsonDocument()) :
                bsonDocument.GetValue(fields[0], bsonString);

            for (int i = 1; i < fields.Length; i++)
            {
                bsonValue = i < field.Length ?
                    bsonValue.AsBsonDocument.GetValue(fields[i], new BsonDocument()) :
                    bsonValue.AsBsonDocument.GetValue(fields[i], bsonString);
            }

            if (bsonValue.IsString) return bsonValue.AsString;
            else if (bsonValue.IsInt32) return bsonValue.AsInt32.ToString();
            else if (bsonValue.IsDouble) return bsonValue.AsDouble.ToString();
            else throw new Exception("Cannot convert to dtring");
        }

        string GetStringValue(BsonDocument bsonDocument, string field)
        {
            return this.GetStringValue(bsonDocument, field, new BsonString(""));
        }

        double GetDoubleValue(BsonDocument bsonDocument, string field)
        {
            BsonValue bsonValue;
            string[] fields = field.Split(".");

            bsonValue = fields.Length > 1 ?
                bsonDocument.GetValue(fields[0], new BsonDocument()) :
                bsonDocument.GetValue(fields[0], new BsonDouble(0));

            for (int i = 1; i < fields.Length; i++)
            {
                bsonValue = i < field.Length ?
                    bsonValue.AsBsonDocument.GetValue(fields[i], new BsonDocument()) :
                    bsonValue.AsBsonDocument.GetValue(fields[i], new BsonDouble(0));
            }

            if (bsonValue.IsString) return double.Parse(bsonValue.AsString);
            else if (bsonValue.IsInt32) return bsonValue.AsInt32;
            else if (bsonValue.IsDouble) return bsonValue.AsDouble;
            else throw new Exception("Cannot convert to double");
        }
    }
}