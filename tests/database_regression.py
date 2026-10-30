import os, subprocess
pg=os.environ['LOCALAPPDATA']+'/NotebookYt/pg-minimal/pgsql/bin/psql.exe'
base=[pg,'-h','127.0.0.1','-p','55433','-U','postgres','-d','editorial','-v','ON_ERROR_STOP=1','-c']
checks=[('weekday uniqueness', '''INSERT INTO "Niches" SELECT gen_random_uuid(), "ChannelId", "Weekday", "Name", "Description", "Guidance", "Enabled" FROM "Niches" LIMIT 1'''),('weekday range', '''UPDATE "Niches" SET "Weekday"=8 WHERE "Weekday"=1'''),('channel foreign key', '''UPDATE "Niches" SET "ChannelId"=gen_random_uuid() WHERE "Weekday"=1'''),('required values', '''UPDATE "Niches" SET "Name"=NULL WHERE "Weekday"=1''')]
for label,sql in checks:
    result=subprocess.run(base+['BEGIN; '+sql+'; ROLLBACK;'],capture_output=True,text=True)
    assert result.returncode != 0, label
    print('PASS',label,result.stderr.strip().splitlines()[0])
