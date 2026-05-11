using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace ARPG.Data
{
    /// <summary>
    /// 프로젝트 전체에서 공유하는 JSON 직렬화 설정.
    /// enum을 이름(string)으로 저장하므로 enum 인덱스/명시값 변경에 데이터가 영향받지 않는다.
    /// AllowIntegerValues=true(기본)이라 옛 int 저장본도 호환됨.
    /// </summary>
    public static class JsonSettings
    {
        private static JsonSerializerSettings _default;

        public static JsonSerializerSettings Default
        {
            get
            {
                if (_default == null)
                {
                    _default = new JsonSerializerSettings
                    {
                        NullValueHandling = NullValueHandling.Include,
                        DefaultValueHandling = DefaultValueHandling.Include,
                    };
                    _default.Converters.Add(new StringEnumConverter());
                }
                return _default;
            }
        }
    }
}
