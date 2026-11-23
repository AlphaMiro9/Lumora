namespace Lumora.Shell;

/// <summary>
/// Lumoras eigener Player. Die Seite bettet YouTubes offiziellen Einbettungs-Player (IFrame-API) ein,
/// blendet dessen Bedienelemente aus und zeigt stattdessen eigene. Werbung im Player wird nicht verändert.
/// Sponsor-Segmente (SponsorBlock, sponsor.ajay.app, CC BY-NC-SA 4.0) werden optional übersprungen.
/// </summary>
public static class PlayerPage
{
    public static void Write(string folder)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "player.html"), Html);
    }

    private const string Html = """
<!doctype html>
<html lang="de"><head><meta charset="utf-8"><title>Lumora Player</title>
<meta name="viewport" content="width=device-width,initial-scale=1">
<style>
:root{color-scheme:dark}
*{box-sizing:border-box}
html,body{height:100%;margin:0;background:#0b1317;color:#e8f1f3;font-family:'Segoe UI',sans-serif}
#wrap{display:flex;flex-direction:column;height:100%;background:#0b1317;position:relative}
#stage{flex:1;min-height:0;background:#000;position:relative}
#player{position:absolute;top:0;left:0;width:100%;height:100%;border:0}
#bar{display:flex;align-items:center;gap:10px;padding:8px 12px;background:#14232a}
button,select{font:inherit;color:#fff;background:#0b7a87;border:0;border-radius:8px;padding:6px 12px;cursor:pointer}
button:disabled{opacity:.5;cursor:default}
button:focus-visible,select:focus-visible,input:focus-visible,a:focus-visible{outline:3px solid #f59e0b;outline-offset:2px}
input[type=range]{accent-color:#f59e0b}
#seek{flex:1}#vol{width:90px}#time{min-width:110px;text-align:center;font-variant-numeric:tabular-nums}
#info{padding:6px 14px 10px;background:#14232a;font-size:13px;color:#9db3ba;display:flex;gap:18px;flex-wrap:wrap;align-items:center}
#title{font-weight:600;color:#e8f1f3;font-size:14px}
a{color:#7dd3dc}
button[aria-pressed=true]{background:#f59e0b;color:#14232a;font-weight:600}
#edge{display:none;position:absolute;left:0;right:0;bottom:0;height:28px;z-index:2}
#wrap.fs #info{display:none}
#wrap.fs #bar{position:absolute;left:0;right:0;bottom:0;z-index:3;background:rgba(20,35,42,.92);transition:opacity .25s}
#wrap.fs.idle #bar{opacity:0;pointer-events:none}
#wrap.fs.idle{cursor:none}
#wrap.fs.idle #edge{display:block}
@media (max-width:700px){#bar{flex-wrap:wrap}#seek{order:5;flex:1 1 100%}#time{min-width:0}label[for=rate]{display:none}#vol{width:70px}button,select{padding:10px 14px}#info{gap:10px}}
#err{display:none;position:absolute;inset:0;background:#0b1317;align-items:center;justify-content:center;flex-direction:column;gap:14px;padding:24px;text-align:center;z-index:2}
</style></head>
<body><div id="wrap">
<div id="stage"><div id="player"></div>
<div id="edge"></div><div id="err" role="alert"><div id="errtext"></div><button id="raw">Auf YouTube ansehen</button></div></div>
<div id="bar">
<button id="play" disabled aria-label="Abspielen oder pausieren">&#9654;</button>
<input id="seek" type="range" min="0" max="1000" value="0" disabled aria-label="Position">
<span id="time">0:00 / 0:00</span>
<label for="rate">Tempo</label>
<select id="rate" aria-label="Wiedergabe-Tempo"><option value="0.5">0,5×</option><option value="0.75">0,75×</option><option value="1" selected>1×</option><option value="1.25">1,25×</option><option value="1.5">1,5×</option><option value="1.75">1,75×</option><option value="2">2×</option></select>
<input id="vol" type="range" min="0" max="100" value="100" aria-label="Lautstärke">
<button id="cc" aria-pressed="false" aria-label="Untertitel für dieses Video" title="Untertitel für dieses Video an/aus (falls vorhanden)">UT</button>
<button id="fs" aria-label="Vollbild">&#9974;</button>
</div>
<div id="info"><span id="title">Lade …</span></div>
</div>
<script>
var q=new URLSearchParams(location.search),id=q.get('v')||'',useSb=q.get('sb')==='1';
var P=function(m){window.chrome.webview.postMessage(JSON.stringify(m))};
var $=function(i){return document.getElementById(i)};
var player=null,segs=[],dragging=false,tickTimer=0,idleTimer=0;
var ccKey='lumora.cc.'+id,cc=false;
try{cc=localStorage.getItem(ccKey)==='1'}catch(x){}
function fmt(t){t=Math.max(0,Math.floor(t||0));var h=Math.floor(t/3600),m=Math.floor(t%3600/60),s=t%60;
 return (h?h+':'+String(m).padStart(2,'0'):m)+':'+String(s).padStart(2,'0')}
function showErr(msg){$('errtext').textContent=msg;$('err').style.display='flex'}
function toRaw(e){if(e)e.preventDefault();P({type:'raw',id:id})}
$('raw').addEventListener('click',toRaw);

if(!/^[A-Za-z0-9_-]{11}$/.test(id)){showErr('Ungültiges Video.');}
else{var sc=document.createElement('script');sc.src='https://www.youtube.com/iframe_api';
 sc.onerror=function(){showErr('Der YouTube-Player konnte nicht geladen werden. Prüfe deine Internetverbindung.')};document.head.appendChild(sc);}

function build(start){
 var st=$('stage');
 if(player&&player.destroy){try{player.destroy()}catch(x){}}
 player=null;
 if(!$('player')){var d=document.createElement('div');d.id='player';st.insertBefore(d,st.firstChild)}
 player=new YT.Player('player',{videoId:id,width:'100%',height:'100%',
  playerVars:{autoplay:1,controls:0,rel:0,modestbranding:1,playsinline:1,iv_load_policy:3,origin:location.origin,
   cc_load_policy:cc?1:0,cc_lang_pref:'de',hl:'de',fs:0,disablekb:1,start:Math.floor(start||0)},
  events:{onReady:onReady,onStateChange:onState,onError:onError}});
}
window.onYouTubeIframeAPIReady=function(){build(0)};
function updateCc(){$('cc').setAttribute('aria-pressed',cc?'true':'false')}
updateCc();
function onReady(){
 $('play').disabled=false;$('seek').disabled=false;$('cc').disabled=false;
 player.setVolume(+$('vol').value);
 var r=+$('rate').value;if(r!==1)player.setPlaybackRate(r);
 if(!tickTimer)tickTimer=setInterval(tick,250);
 if(useSb&&!segs.length)loadSegments();
}
function onState(e){
 $('play').innerHTML=e.data===1?'&#10074;&#10074;':'&#9654;';
 var d=player.getVideoData&&player.getVideoData();
 if(d&&d.title){$('title').textContent=d.title;document.title=d.title}
}
function onError(e){
 var msg=(e.data===101||e.data===150)?'Der Urheber erlaubt dieses Video nicht im eingebetteten Player.'
  :(e.data===100)?'Dieses Video ist nicht mehr verfügbar.'
  :'Das Video konnte hier nicht abgespielt werden (Fehler '+e.data+').';
 showErr(msg+' Du kannst es direkt auf YouTube ansehen.');
}
function tick(){
 if(!player||!player.getCurrentTime)return;
 var cur=player.getCurrentTime(),dur=player.getDuration()||0;
 if(!dragging&&dur>0)$('seek').value=Math.round(cur/dur*1000);
 $('time').textContent=fmt(cur)+' / '+fmt(dur);
 for(var i=0;i<segs.length;i++){var s=segs[i];
  if(cur>=s[0]&&cur<s[1]-0.4){player.seekTo(s[1],true);
   break}}
}
function loadSegments(){
 var url='https://sponsor.ajay.app/api/skipSegments?videoID='+id+'&categories='+encodeURIComponent('["sponsor","selfpromo","interaction"]');
 fetch(url).then(function(r){return r.ok?r.json():[]}).then(function(a){
  segs=(a||[]).filter(function(x){return x&&x.segment&&x.segment.length===2}).map(function(x){return x.segment});
 }).catch(function(){});
}
function toggle(){if(!player)return;player.getPlayerState()===1?player.pauseVideo():player.playVideo()}
function fullscreen(){var w=$('wrap');if(document.fullscreenElement)document.exitFullscreen();else w.requestFullscreen()}
$('play').addEventListener('click',toggle);
$('fs').addEventListener('click',fullscreen);
$('cc').disabled=true;
$('cc').addEventListener('click',function(){
 if(!player)return;
 cc=!cc;try{localStorage.setItem(ccKey,cc?'1':'0')}catch(x){}
 updateCc();$('cc').disabled=true;
 var t=player.getCurrentTime?player.getCurrentTime():0;build(t);
});
var wrap=$('wrap');
function wake(){
 wrap.classList.remove('idle');clearTimeout(idleTimer);
 if(!document.fullscreenElement)return;
 idleTimer=setTimeout(function(){if($('bar').matches(':hover'))return wake();wrap.classList.add('idle')},3000);
}
document.addEventListener('fullscreenchange',function(){wrap.classList.toggle('fs',!!document.fullscreenElement);wake()});
['mousemove','pointerdown','keydown','touchstart'].forEach(function(n){document.addEventListener(n,wake,true)});
$('edge').addEventListener('mousemove',wake);
$('rate').addEventListener('change',function(){if(player)player.setPlaybackRate(+this.value)});
$('vol').addEventListener('input',function(){if(player)player.setVolume(+this.value)});
$('seek').addEventListener('input',function(){dragging=true;var d=player.getDuration()||0;$('time').textContent=fmt(this.value/1000*d)+' / '+fmt(d)});
$('seek').addEventListener('change',function(){var d=player.getDuration()||0;player.seekTo(this.value/1000*d,true);dragging=false});
document.addEventListener('keydown',function(e){
 if(!player||e.target.tagName==='SELECT'||e.target.type==='range')return;
 if(e.key===' '){e.preventDefault();toggle()}
 else if(e.key==='ArrowRight'){player.seekTo(player.getCurrentTime()+5,true)}
 else if(e.key==='ArrowLeft'){player.seekTo(player.getCurrentTime()-5,true)}
 else if(e.key==='f'||e.key==='F'){fullscreen()}
 else if(e.key==='c'||e.key==='C'){if(!$('cc').disabled)$('cc').click()}
});
</script></body></html>
""";
}
