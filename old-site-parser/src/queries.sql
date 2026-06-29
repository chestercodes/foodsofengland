select *
from Step1ScannedFiles sf
left join  Step2ParsedUrls pu
on (sf.RelativePath = pu.DiskPath or sf.RelativePath = pu.RenamedUrl)
where FileType = 'image'
and pu.RawUrl is null
order by sf.RelativePath