using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Popcorn.Domain.Models;

public class ItemHistoryDto : ItemHistory
{
    public string Title { get; set; }
}
