﻿using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace vws.web.Models._task
{
    public class AddAttachmentModel
    {
        [Required]
        public long Id { get; set; }
        [Required]
        public List<Guid> Attachments { get; set; }
    }
}
