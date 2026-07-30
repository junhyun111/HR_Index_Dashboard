using HRDashboard.Data;
using HRDashboard.Models;
using Microsoft.EntityFrameworkCore;

namespace HRDashboard.Services;

public sealed record OrganizationNodeResponse(
    string Id,string Name,string Type,string? ParentId,int Order,int LayoutX,int LayoutY);
public sealed record OrganizationResponse(
    IReadOnlyList<OrganizationNodeResponse> Items,DateTimeOffset? UpdatedAtUtc,string? UpdatedBy);

public sealed class OrganizationService(OrganizationDbContext db)
{
    private static readonly HashSet<string> Types=
        ["ceo","executive","group","office","division","center","team","taskforce","council"];

    public async Task<OrganizationResponse> GetAsync(CancellationToken ct=default)
    {
        var items=await db.OrganizationNodes.AsNoTracking()
            .OrderBy(x=>x.ParentId).ThenBy(x=>x.DisplayOrder).ThenBy(x=>x.Name)
            .Select(x=>new OrganizationNodeResponse(x.Id,x.Name,x.Type,x.ParentId,x.DisplayOrder,x.LayoutX,x.LayoutY))
            .ToArrayAsync(ct);
        var state=await db.OrganizationStates.AsNoTracking().FirstOrDefaultAsync(x=>x.Id==1,ct);
        return new(items,state?.UpdatedAtUtc,state?.UpdatedBy);
    }

    public async Task<OrganizationResponse> SaveAsync(
        IReadOnlyCollection<OrganizationNodeResponse>? requested,string userName,CancellationToken ct=default)
    {
        var items=requested?.Select(x=>new OrganizationNodeResponse(
            x.Id?.Trim()??"",x.Name?.Trim()??"",x.Type?.Trim().ToLowerInvariant()??"",
            string.IsNullOrWhiteSpace(x.ParentId)?null:x.ParentId.Trim(),
            x.Order,Math.Clamp(x.LayoutX,-10000,10000),Math.Clamp(x.LayoutY,-10000,10000))).ToArray()??[];
        Validate(items);
        await using var transaction=await db.Database.BeginTransactionAsync(ct);
        await db.OrganizationNodes.ExecuteDeleteAsync(ct);
        db.OrganizationNodes.AddRange(items.Select(x=>new OrganizationNode
        {
            Id=x.Id,Name=x.Name,Type=x.Type,ParentId=x.ParentId,
            DisplayOrder=x.Order,LayoutX=x.LayoutX,LayoutY=x.LayoutY
        }));
        var now=DateTimeOffset.UtcNow;
        var state=await db.OrganizationStates.FindAsync([1],ct);
        if(state==null)db.OrganizationStates.Add(new OrganizationState{Id=1,UpdatedAtUtc=now,UpdatedBy=userName});
        else{state.UpdatedAtUtc=now;state.UpdatedBy=userName;}
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return await GetAsync(ct);
    }

    private static void Validate(IReadOnlyCollection<OrganizationNodeResponse> items)
    {
        if(items.Count==0)throw new ArgumentException("조직도를 한 개 이상 등록해 주세요.");
        if(items.Count>500)throw new ArgumentException("조직은 최대 500개까지 등록할 수 있습니다.");
        if(items.Any(x=>x.Id.Length is 0 or >100))throw new ArgumentException("조직 ID 형식을 확인해 주세요.");
        if(items.Any(x=>x.Name.Length is 0 or >40))throw new ArgumentException("조직명은 1~40자로 입력해 주세요.");
        if(items.Any(x=>!Types.Contains(x.Type)))throw new ArgumentException("지원하지 않는 조직 유형이 포함되어 있습니다.");
        var duplicate=items.GroupBy(x=>x.Id,StringComparer.OrdinalIgnoreCase).FirstOrDefault(x=>x.Count()>1);
        if(duplicate!=null)throw new ArgumentException($"조직 ID가 중복되었습니다: {duplicate.Key}");
        var byId=items.ToDictionary(x=>x.Id,StringComparer.OrdinalIgnoreCase);
        if(items.Count(x=>x.ParentId==null)!=1)throw new ArgumentException("최상위 조직은 정확히 한 개여야 합니다.");
        if(items.Any(x=>x.ParentId!=null&&!byId.ContainsKey(x.ParentId)))
            throw new ArgumentException("존재하지 않는 상위 조직이 지정되어 있습니다.");
        foreach(var item in items)
        {
            var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase){item.Id};
            var current=item;
            while(current.ParentId!=null)
            {
                if(!seen.Add(current.ParentId))throw new ArgumentException("조직도에 순환 참조가 있습니다.");
                current=byId[current.ParentId];
            }
        }
    }
}
