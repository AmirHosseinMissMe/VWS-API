﻿using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace vws.web.Models
{
    public class UsernameModel
    {
        [Required]
        public string Username { get; set; }
    }
}
