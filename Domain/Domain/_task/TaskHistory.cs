﻿using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Threading.Tasks;

namespace vws.web.Domain._task
{
    [Table("Task_TaskHistory")]
    public class TaskHistory
    {
        public TaskHistory()
        {
            TaskHistoryParameters = new HashSet<TaskHistoryParameter>();
        }

        public long Id { get; set; }

        public long GeneralTaskId { get; set; }

        public string EventBody { get; set; }

        public DateTime EventTime { get; set; }

        public virtual GeneralTask GeneralTask { get; set; }

        public virtual ICollection<TaskHistoryParameter> TaskHistoryParameters { get; set; }
    }
}
