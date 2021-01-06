﻿using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace vws.web.Domain.chat
{
    [Table("Message", Schema = "chat")]
    public class Message
    {
        [Key]
        public long Id { get; set; }

        [MaxLength(4000)]
        public string Body { get; set; }

        public byte MessageTypeId { get; set; }

        public virtual MessageType MessageType { get; set; }

    }
}
