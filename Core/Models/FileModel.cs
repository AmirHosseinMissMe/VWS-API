﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace vws.web.Models
{
    public class FileModel
    {
        public string Name { get; set; }
        public string Extension { get; set; }
        public long Size { get; set; }
        public Guid FileContainerGuid { get; set; }
    }
}
