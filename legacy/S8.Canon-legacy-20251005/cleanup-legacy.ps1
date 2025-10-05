param(
  [string]$Db = 's8',
  [string]$Mongo = 's8-mongo'
)

$collections = @(
  'Koan.Canon.Model.CanonicalProjectionView#canonical',
  'Koan.Canon.Model.KeyIndex',
  'Koan.Canon.Model.LineageProjectionView#lineage',
  'Koan.Canon.Model.PolicyBundle',
  'Koan.Canon.Model.ProjectionTask',
  'Koan.Canon.Model.Record#intake',
  'Koan.Canon.Model.Record#keyed',
  'Koan.Canon.Model.Record#standardized',
  'Koan.Canon.Model.ReferenceItem',
  'Koan.Canon.Model.RejectionReport',
  'S8.Canon.Shared.Device#flow.device.intake'
)

foreach ($c in $collections) {
  $eval = "if (db.getCollectionNames().includes('$c')) db.getCollection('$c').drop()"
  Write-Host "Dropping $c if exists..."
  docker exec $Mongo mongosh $Db --quiet --eval $eval | Out-Null
}

Write-Host "Remaining collections:" -ForegroundColor Cyan
$remaining = docker exec $Mongo mongosh $Db --quiet --eval 'JSON.stringify(db.getCollectionNames().sort())'
Write-Output $remaining
