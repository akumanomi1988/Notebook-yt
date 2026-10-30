import concurrent.futures as futures
import json
import unittest
import urllib.request
import urllib.error
import uuid

BASE = 'http://127.0.0.1:5080/api'
def request(path, method='GET', body=None, headers=None):
    req = urllib.request.Request(BASE + path, data=json.dumps(body).encode() if body is not None else None,
        method=method, headers={'Content-Type': 'application/json', **(headers or {})})
    try:
        with urllib.request.urlopen(req) as response:
            return response.status, json.loads(response.read() or b'null')
    except urllib.error.HTTPError as error:
        return error.code, json.loads(error.read() or b'null')

class ApiRegression(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        # Run against a dedicated empty audit database.
        with futures.ThreadPoolExecutor(8) as pool:
            results = list(pool.map(lambda _: request('/channel', 'PUT', {'name':'Audit','timeZone':'Europe/Madrid'}), range(8)))
        assert all(code == 200 for code, _ in results), results
        cls.niches = request('/week')[1]
        assert len(cls.niches) == 7 and len({n['weekday'] for n in cls.niches}) == 7

    def create(self):
        code, value = request('/productions', 'POST', {'contentNicheId':self.niches[0]['id'], 'topic':'Audit '+str(uuid.uuid4()), 'plannedDate':'2026-10-30'})
        self.assertEqual(code, 201, value)
        self.assertEqual(value['status'], 'Draft')
        self.assertTrue(value['createdAtUtc'].endswith('Z'))
        return value['id']

    def test_null_fields_and_timezone(self):
        for zone in [None, 'Invalid/Zone', 'Romance Standard Time']:
            self.assertEqual(request('/channel','PUT',{'name':'Audit','timeZone':zone})[0],400)
        niche = dict(self.niches[0], description=None)
        code, error = request('/week/1','PUT',niche)
        self.assertEqual(code,400)
        self.assertIn('errors',error)

    def test_snapshot_and_disabled(self):
        pid=self.create()
        original=request('/productions/'+pid)[1]['production']['nicheNameSnapshot']
        niche=dict(self.niches[0], name='Changed', enabled=False)
        self.assertEqual(request('/week/1','PUT',niche)[0],200)
        self.assertEqual(request('/productions','POST',{'contentNicheId':niche['id'],'topic':'Disabled','plannedDate':'2026-10-30'})[0],400)
        self.assertEqual(request('/productions/'+pid)[1]['production']['nicheNameSnapshot'],original)
        self.assertEqual(request('/week/1','PUT',dict(self.niches[0],enabled=True))[0],200)

    def test_concurrent_status_and_cancelled_immutability(self):
        pid=self.create()
        path='/productions/'+pid
        with futures.ThreadPoolExecutor(8) as pool:
            results=list(pool.map(lambda _:request(path+'/status','POST',{'status':'BriefReady'})[0], range(8)))
        self.assertEqual(results.count(200),1,results)
        self.assertEqual(results.count(409),7,results)
        self.assertEqual(len(request(path)[1]['history']),2)
        self.assertEqual(request(path+'/status','POST',{})[0],400)
        for invalid in [99,'Unknown',None]:
            self.assertEqual(request(path+'/status','POST',{'status':invalid})[0],400)
        self.assertEqual(request(path+'/status','POST',{'status':'Cancelled'})[0],200)
        self.assertEqual(request(path,'PUT',{'topic':'Changed','plannedDate':'2026-11-01'})[0],409)
        self.assertEqual(request(path+'/notebook','PUT',{'url':'https://notebooklm.google.com/notebook/audit'})[0],409)
        self.assertEqual(request(path+'/status','POST',{'status':'Draft'})[0],409)

    def test_notebook_urls_and_duplicate_put(self):
        path='/productions/'+self.create()+'/notebook'
        for url in [None,'http://notebooklm.google.com/notebook/id','https://evil.test/notebook/id','https://notebooklm.google.com/notebook','https://user@notebooklm.google.com/notebook/id','https://notebooklm.google.com:999/notebook/id']:
            self.assertEqual(request(path,'PUT',{'url':url})[0],400,url)
        body={'url':'https://notebooklm.google.com/notebook/audit'}
        with futures.ThreadPoolExecutor(8) as pool:
            results=list(pool.map(lambda _:request(path,'PUT',body), range(8)))
        self.assertTrue(all(c==200 for c,_ in results),results)
        self.assertEqual(len({v['id'] for _,v in results}),1)

    def test_errors_and_cross_origin(self):
        code,error=request('/productions/'+str(uuid.uuid4()))
        self.assertEqual(code,404)
        self.assertEqual(error['status'],404)
        self.assertEqual(request('/channel','PUT',{'name':'Attack','timeZone':'Etc/UTC'},{'Origin':'https://evil.test'})[0],403)

if __name__ == '__main__': unittest.main(verbosity=2)
