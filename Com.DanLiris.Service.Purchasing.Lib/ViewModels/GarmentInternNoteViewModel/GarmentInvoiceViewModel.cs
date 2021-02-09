﻿using Com.DanLiris.Service.Purchasing.Lib.Models.GarmentInvoiceModel;
using System;
using System.Collections.Generic;
using System.Text;

namespace Com.DanLiris.Service.Purchasing.Lib.ViewModels.GarmentInternNoteViewModel
{
    public class GarmentInvoiceInternNoteViewModel
    {
        public GarmentInvoice GarmentInvoices { get; set; }
        public CategoryDto Category { get; set; }
        public string PaymentMethod { get; internal set; }
    }
}
