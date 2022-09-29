using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using ILRuntime.Runtime.Enviorment;
using ILRuntime.CLR.TypeSystem;
using ILRuntime.CLR.Method;

// 下面这行为了取消使用WWW的警告，Unity2018以后推荐使用UnityWebRequest，处于兼容性考虑Demo依然使用WWW
#pragma warning disable CS0618
public class HelloWorld : MonoBehaviour {
    // AppDomain是ILRuntime的入口，最好是在一个单例类中保存，整个游戏全局就一个，这里为了示例方便，每个例子里面都单独做了一个
    // 大家在正式项目中请全局只创建一个AppDomain
    AppDomain appdomain;
    System.IO.MemoryStream fs;
    System.IO.MemoryStream p;
    
    void Start() {
        StartCoroutine(LoadHotFixAssembly());
    }

    IEnumerator LoadHotFixAssembly() {
        // 首先实例化ILRuntime的AppDomain，AppDomain是一个应用程序域，每个AppDomain都是一个独立的沙盒SandBox
        appdomain = new ILRuntime.Runtime.Enviorment.AppDomain();
        
        // 正常项目中应该是自行从其他地方下载dll，或者打包在AssetBundle中读取，平时开发以及为了演示方便直接从StreammingAssets中读取，
        // 正式发布的时候需要大家自行从其他地方读取dll
        // !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
        // 这个DLL文件是直接编译HotFix_Project.sln生成的，已经在项目中设置好输出目录为StreamingAssets，在VS里直接编译即可生成到对应目录，无需手动拷贝
        // 工程目录在Assets\Samples\ILRuntime\1.6\Demo\HotFix_Project~
        // 以下加载写法只为演示，并没有处理在编辑器切换到Android平台的读取，需要自行修改
#if UNITY_ANDROID
        WWW www = new WWW(Application.streamingAssetsPath + "/HotFix_Project.dll");
#else
        WWW www = new WWW("file:" + Application.streamingAssetsPath + "/HotFix_Project.dll");
#endif
        while (!www.isDone) // 协程的方便之处就在于：可以控制等待到相应的步骤执行完毕，比如这里一定等到相应的热更新程序包加载完毕
            yield return null;
        if (!string.IsNullOrEmpty(www.error))
            UnityEngine.Debug.LogError(www.error);
        byte[] dll = www.bytes;
        www.Dispose();
        // PDB文件是调试数据库，如需要在日志中显示报错的行号，则必须提供PDB文件，不过由于会额外耗用内存，正式发布时请将PDB去掉，下面LoadAssembly的时候pdb传null即可
#if UNITY_ANDROID
        www = new WWW(Application.streamingAssetsPath + "/HotFix_Project.pdb");
#else
        www = new WWW("file:" + Application.streamingAssetsPath + "/HotFix_Project.pdb");
#endif
        while (!www.isDone)
            yield return null;
        if (!string.IsNullOrEmpty(www.error))
            UnityEngine.Debug.LogError(www.error);
        byte[] pdb = www.bytes;
        fs = new MemoryStream(dll);
        p = new MemoryStream(pdb);
        try {
            appdomain.LoadAssembly(fs, p, new ILRuntime.Mono.Cecil.Pdb.PdbReaderProvider());
        }
        catch {
            Debug.LogError("加载热更DLL失败，请确保已经通过VS打开Assets/Samples/ILRuntime/1.6/Demo/HotFix_Project/HotFix_Project.sln编译过热更DLL");
        }
        InitializeILRuntime();
        OnHotFixLoaded();
    }

    void InitializeILRuntime() {
#if DEBUG && (UNITY_EDITOR || UNITY_ANDROID || UNITY_IPHONE)
        // 由于Unity的Profiler接口只允许在主线程使用，为了避免出异常，需要告诉ILRuntime主线程的线程ID才能正确将函数运行耗时报告给Profiler
        appdomain.UnityMainThreadID = System.Threading.Thread.CurrentThread.ManagedThreadId;
#endif
        // 这里做一些ILRuntime的注册，HelloWorld示例暂时没有需要注册的
    }

    void OnHotFixLoaded() {

// 调用无参数静态方法
        // HelloWorld，第一次方法调用: 
        appdomain.Invoke("HotFix_Project.InstanceClass", "StaticFunTest", null, null);

// 调用带参数的静态方法
        // 调用带参数的静态方法,appdomain.Invoke("类名", "方法名", 对象引用, 参数列表);
        appdomain.Invoke("HotFix_Project.InstanceClass", "StaticFunTest2", null, 123);

// 通过IMethod调用方法        
        // 预先获得IMethod，可以减低每次调用查找方法耗用的时间
        IType type = appdomain.LoadedTypes["HotFix_Project.InstanceClass"];
        // 根据方法名称和参数个数获取方法
        IMethod method = type.GetMethod("StaticFunTest2", 1);
        appdomain.Invoke(method, null, 123);

        
// 通过无GC Alloc方式调用方法：　下面两行代码接下来的几信方法通用共用
        // // 预先获得IMethod，可以减低每次调用查找方法耗用的时间
        // IType type = appdomain.LoadedTypes["HotFix_Project.InstanceClass"];
        // // 根据方法名称和参数个数获取方法
        // IMethod method = type.GetMethod("StaticFunTest2", 1); // 这之上的都与前面的有重复,是一样的

// 1.指定参数类型获取IMethod
        using (var ctx = appdomain.BeginInvoke(method)) {
            ctx.PushInteger(123);
            ctx.Invoke();
        }
        Debug.Log("指定参数类型来获得IMethod");
        IType intType = appdomain.GetType(typeof(int)); // 接下来是：把方法的参数全部封装到了一个参数链表里，将链表传给方法再调用
        // 参数类型列表
        List<IType> paramList = new List<ILRuntime.CLR.TypeSystem.IType>();
        paramList.Add(intType);
        // 根据方法名称和参数类型列表获取方法
        method = type.GetMethod("StaticFunTest2", paramList, null);
        appdomain.Invoke(method, null, 456);

// 2.调用成员方法：这里还有一点儿不是太明白        
        object obj = appdomain.Instantiate("HotFix_Project.InstanceClass", new object[] { 233 });
        // 第二种方式,通过反射实例化
        object obj2 = ((ILType)type).Instantiate();　// 拿到一个热更新类的实例
        // 然后通过反射调用成员方法
        method = type.GetMethod("get_ID", 0);　// 这里是说，调用了ID getter,这里拿到的id = 0, 这行有点儿没看懂。。。。。
        using (var ctx = appdomain.BeginInvoke(method)) {
            ctx.PushObject(obj); // 参数Object[]数组
            ctx.Invoke();　// 带参数的非静态调用
            int id = ctx.ReadInteger();
            Debug.Log("!! 1。HotFix_Project.InstanceClass.ID = " + id);
        }
        using (var ctx = appdomain.BeginInvoke(method)) {
            ctx.PushObject(obj2);
            ctx.Invoke();
            int id = ctx.ReadInteger();
            Debug.Log("!! 2。HotFix_Project.InstanceClass.ID = " + id);
        }

        
        // Debug.Log("指定参数类型来获得IMethod");
        // IType intType = appdomain.GetType(typeof(int)); // 接下来是：把方法的参数全部封装到了一个参数链表里，将链表传给方法再调用
        // // 参数类型列表
        // List<IType> paramList = new List<ILRuntime.CLR.TypeSystem.IType>();
        // paramList.Add(intType);
// 3. 调用泛型方法
        // 1）直接通过appdomain.InvokeGenericMethod来调用
        IType stringType = appdomain.GetType(typeof(string));
        IType[] genericArguments = new IType[] { stringType };
        appdomain.InvokeGenericMethod("HotFix_Project.InstanceClass", "GenericMethod", genericArguments, null, "TestString");
        // 2）通过获取泛型方法的IMethod来调用
        paramList.Clear(); // 并不确定前面曾经做过什么，所以先把它清空
        paramList.Add(intType);
        genericArguments = new IType[] { intType };
        method = type.GetMethod("GenericMethod", paramList, genericArguments);
        appdomain.Invoke(method, null, 33333);　// 调用的时候传入int 的值

        // object obj = appdomain.Instantiate("HotFix_Project.InstanceClass", new object[] { 233 });
// 4.调用带Ref/Out参数的方法
    // public void RefOutMethod(int addition, out List<int> lst, ref int val) {　// 热更新里的方法申明
        method = type.GetMethod("RefOutMethod", 3); // 参数有三个
        int initialVal = 500;
        using(var ctx = appdomain.BeginInvoke(method)) {
            // 第一个ref/out参数初始值
            ctx.PushObject(null);
            // 第二个ref/out参数初始值
            ctx.PushInteger(initialVal); // ref int所以压入的是PushInteger吗？
            // 压入this
            ctx.PushObject(obj);
            // 压入参数1:addition
            ctx.PushInteger(100);
            // 压入参数2: lst,由于是ref/out，需要压引用，这里是引用0号位，也就是第一个PushObject的位置
            ctx.PushReference(0);
            // 压入参数3,val，同ref/out
            ctx.PushReference(1);
            ctx.Invoke();
            // 读取0号位的值
            List<int> lst = ctx.ReadObject<List<int>>(0);
            initialVal = ctx.ReadInteger(1);
            Debug.Log(string.Format("lst[0]={0}, initialVal={1}", lst[0], initialVal));
        }
    }
    
    private void OnDestroy() {
        if (fs != null)
            fs.Close();
        if (p != null)
            p.Close();
        fs = null;
        p = null;
    }
    void Update() {}
}



