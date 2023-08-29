import axios from 'axios';
// axios.defaults.withCredentials = true;

export default class ApiService {

    static getConfig(authToken, optionalHeaders=null, optionalConfig=null) {
        const config = {
            headers: {
                Authorization: `Bearer ${authToken}`,
                ...optionalHeaders
            },
            ...optionalConfig
        }
        return config;
    }

    static getCsrfHeader() {
        const csrf = document.querySelector("meta[name='csrf-token']").getAttribute("content");
        return {
            'X-CSRF-Token': csrf
        }
    }

    static async apiGetRequest(authToken, uri, optionalHeaders=null, optionalConfig=null) {
        return await axios.get(uri, this.getConfig(authToken, optionalHeaders, optionalConfig));
    }

    static async apiPostRequest(authToken, uri, data, addCsrf, optionalHeaders=null) {
        let headers;
        if (addCsrf) {
            headers = {
                ...optionalHeaders,
                ...this.getCsrfHeader()
            }
        } else {
            headers = {...optionalHeaders}
        }
        return await axios.post(uri, data, this.getConfig(authToken, headers));
    }
}